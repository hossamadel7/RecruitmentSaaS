"""Owner-authorized paused service identity setup; never impersonates a CRM user.

Run with a private selected-pilot-scope.json path. All model/dispatch gates must
already be explicitly false. Knowledge creation/approval remains an authenticated
admin action. The remote operation is idempotent and keeps recovery files private.
"""
import json
import pathlib
import subprocess
import sys
import uuid

scope = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8-sig"))
tenant, account, manager, branch = [str(uuid.UUID(scope[k])) for k in
    ("reservedTenantId", "accountId", "currentTeamManagerId", "currentBranchId")]
if scope["accountName"] not in ("Bakar", "Sherif") or not all(scope[k] for k in
        ("branchConfirmed", "draftReviewerConfirmed", "knowledgeApproverConfirmed")):
    raise SystemExit("Confirmed account scope required")
account_name = scope['accountName']
agent = str(uuid.uuid5(uuid.UUID(tenant), "paused-" + account_name.lower() + "-service-identity"))
limit = 5 if account_name == 'Bakar' else 1
sql = f'''BEGIN;
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('paused-bakar-setup-{tenant}'));
DO $guard$ BEGIN
IF NOT EXISTS (SELECT 1 FROM demorecruitment."WhatsAppAccounts" WHERE "Id"='{account}' AND "Name"='{account_name}' AND "IsActive")
OR NOT EXISTS (SELECT 1 FROM demorecruitment."Users" WHERE "Id"='{manager}' AND "Role"=7 AND "IsActive" AND "BranchId"='{branch}')
OR (SELECT count(DISTINCT f."ManagerId") FROM demorecruitment."TeamLeadForms" f
 JOIN demorecruitment."Users" u ON u."Id"=f."ManagerId" AND u."IsActive"
 JOIN demorecruitment."WhatsAppAccounts" a ON a."Id"='{account}'
 WHERE regexp_replace(f."WhatsAppNumber"::text,'[^0-9]','','g')=regexp_replace(a."DisplayPhoneNumber"::text,'[^0-9]','','g')) <> 1
OR NOT EXISTS (SELECT 1 FROM demorecruitment."TeamLeadForms" f JOIN demorecruitment."WhatsAppAccounts" a ON a."Id"='{account}'
 WHERE f."ManagerId"='{manager}' AND regexp_replace(f."WhatsAppNumber"::text,'[^0-9]','','g')=regexp_replace(a."DisplayPhoneNumber"::text,'[^0-9]','','g'))
THEN RAISE EXCEPTION 'Account/team/branch mapping changed'; END IF;
IF EXISTS (SELECT 1 FROM demorecruitment."AiSalesAgents" WHERE ("Id"='{agent}' OR "WhatsAppAccountId"='{account}')
 AND NOT ("Id"='{agent}' AND "TenantId"='{tenant}' AND "TeamManagerId"='{manager}' AND "WhatsAppAccountId"='{account}' AND "Mode"=1 AND "Paused" AND "MaxActiveConversations"={limit}))
THEN RAISE EXCEPTION 'Existing agent conflicts with paused setup'; END IF;
END $guard$;
WITH inserted AS (
INSERT INTO demorecruitment."AiSalesAgents" ("Id","TenantId","WhatsAppAccountId","TeamManagerId","DisplayName","Mode","Paused","MaxActiveConversations","Version","CreatedAt")
VALUES ('{agent}','{tenant}','{account}','{manager}','المساعد الافتراضي لشركة الفهد العربي',1,true,{limit},1,now())
ON CONFLICT ("Id") DO NOTHING RETURNING "Id")
INSERT INTO demorecruitment."AuditLogs" ("Id","ActorId","ActorType","EventType","EntityType","EntityId","NewValueJson","CreatedAt")
SELECT gen_random_uuid(),NULL,2,'AiAgentPreparedPaused','AiSalesAgent',"Id",
'{{"source":"owner_authorized_deployment_setup","paused":true,"mode":"Shadow","maxActiveConversations":{limit},"customerAssignments":0,"crmUserImpersonated":false}}',now() FROM inserted;
COMMIT;
'''
remote = r'''
import json, pathlib, subprocess, uuid, datetime, os, shutil
payload = PAYLOAD
envpath = pathlib.Path('/etc/recruitmentsaas/app.env')
old = envpath.read_text()
values = {}
for line in old.splitlines():
    if '=' in line and not line.lstrip().startswith('#'):
        k,v = line.split('=',1); values[k.strip()] = v.strip().strip('"').strip("'")
gates = ['StagingValidation__Enabled','AiSales__Enabled','AiSalesEngine__DraftsEnabled',
'AiSalesEngine__ExternalProcessingApproved','AiSalesDispatch__Enabled','AiSalesDispatch__ReleaseApproved',
'AiVoice__Enabled','AiVoice__ExternalProcessingApproved','AiChatLearning__Enabled','AiOfficeBooking__Enabled']
assert all(values.get(k, '').lower() == 'false' for k in gates), 'AI gates must all be explicitly false'
assert values.get('AiSales__TenantId', '') in ('','00000000-0000-0000-0000-000000000000',payload['tenant']), 'Tenant conflict'
root = pathlib.Path('/home/ubuntu/.local/share/recruitmentsaas-paused-setup') / (datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%d')+'-'+uuid.uuid4().hex)
root.mkdir(parents=True, mode=0o700)
os.chmod(root,0o700)
shutil.copy2(envpath, root/'app.env.before')
os.chmod(root/'app.env.before',0o600)
with (root/'ai-tables-before.sql').open('wb') as f:
    subprocess.run(['runuser','-u','postgres','--','pg_dump','-d','RecruitmentCRM','--data-only',
    '--table=demorecruitment."AiSalesAgents"','--table=demorecruitment."AiSalesKnowledge"'],stdout=f,check=True)
os.chmod(root/'ai-tables-before.sql',0o600)
subprocess.run(['runuser','-u','postgres','--','psql','-X','-q','-v','ON_ERROR_STOP=1','-d','RecruitmentCRM'],
input=payload['sql'],text=True,capture_output=True,check=True)
changed = values.get('AiSales__TenantId') != payload['tenant']
if changed:
    new = '\n'.join(l for l in old.splitlines() if not l.startswith('AiSales__TenantId='))+'\nAiSales__TenantId='+payload['tenant']+'\n'
    temp = envpath.with_name('app.env.paused-setup.tmp')
    temp.write_text(new); os.chmod(temp,0o600); os.chown(temp,0,0); os.replace(temp,envpath)
    try:
        subprocess.run(['systemctl','restart','recruitmentsaas'],check=True)
        subprocess.run(['curl','--fail','--silent','--retry','15','--retry-delay','1','--retry-connrefused','http://127.0.0.1:8082/health'],capture_output=True,check=True)
    except Exception:
        shutil.copy2(root/'app.env.before',envpath)
        subprocess.run(['systemctl','restart','recruitmentsaas'],check=True)
        raise
query = 'SELECT json_build_object(\'agents\',(SELECT count(*) FROM demorecruitment."AiSalesAgents"),\'paused\',(SELECT "Paused" FROM demorecruitment."AiSalesAgents" WHERE "Id"=\''+payload['agent']+'\'),\'assignments\',(SELECT count(*) FROM demorecruitment."AiLeadAssignments"),\'drafts\',(SELECT count(*) FROM demorecruitment."AiSalesDrafts"),\'knowledge\',(SELECT count(*) FROM demorecruitment."AiSalesKnowledge"));'
result = subprocess.run(['runuser','-u','postgres','--','psql','-X','-q','-A','-t','-d','RecruitmentCRM'],input=query,text=True,capture_output=True,check=True)
state = json.loads(result.stdout)
assert state['paused'], 'Agent must remain paused'
state.update({'agentId':payload['agent'],'tenantConfigured':True,'allAiGatesDisabled':True,'recoveryDirectory':str(root),'knowledgeImported':False})
(root/'result.json').write_text(json.dumps(state,indent=2))
os.chmod(root/'result.json',0o600)
print(json.dumps(state))
'''.replace('PAYLOAD', repr({'tenant': tenant, 'agent': agent, 'sql': sql}))
result = subprocess.run(['ssh','-o','BatchMode=yes','-o','StrictHostKeyChecking=yes','-o','ConnectTimeout=20',
    'ubuntu@158.69.206.138','sudo -n python3 -'],input=remote,text=True,encoding='utf-8',capture_output=True)
if result.returncode:
    # Remote Python traceback reports code/context but never print the environment.
    raise SystemExit(result.stderr[-2000:])
state = json.loads(result.stdout)
output = pathlib.Path(sys.argv[1]).parent/('paused-agent-setup-result.json' if account_name == 'Bakar' else account_name.lower()+'-paused-agent-setup-result.json')
output.write_text(json.dumps(state,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in state.items() if k!='recoveryDirectory'}))
