"""Activate exactly the owner's selected conversation; credentials use stdin only.

The caller supplies RECRUITMENT_PILOT_API_KEY transiently after local DPAPI
decryption. Neither credentials nor customer text are printed or put in arguments.
No CRM user is impersonated and this tool never generates or sends a reply.
"""
import json, os, pathlib, subprocess, uuid

root = pathlib.Path(os.environ['LOCALAPPDATA']) / 'RecruitmentSaaS/ReleaseCandidates/2026-10-14-sherif-single-draft-pilot'
scope_root = pathlib.Path(os.environ['LOCALAPPDATA']) / 'RecruitmentSaaS/PilotPreparation/2026-10-14'
selection = json.loads((scope_root/'sherif-assigned-conversation.json').read_text(encoding='utf-8-sig'))
chat = str(uuid.UUID(selection['conversationId']))
agent = str(uuid.UUID(selection['agentId']))
assert selection['controller'] == 3 and selection['paused']
payload = dict(root=(root/'remote-path.txt').read_text().strip(),
    dllSha256=json.loads((root/'manifest.json').read_text())['dllSha256'].lower(),
    chat=chat, agent=agent, version=selection['ownershipVersion'], agentVersion=selection['agentVersion'],
    apiKey=os.environ.get('RECRUITMENT_PILOT_API_KEY',''))
if not payload['apiKey'].startswith('sk-'):
    raise SystemExit('Private API credential unavailable; nothing activated')

remote = r"""
import json,sys,pathlib,subprocess,os,shutil,hashlib,urllib.request,urllib.error
payload=json.load(sys.stdin)
root=pathlib.Path(payload['root'])
assert str(root).startswith('/home/ubuntu/.local/share/recruitmentsaas-installations/')
assert json.loads((root/'result.json').read_text())['installationSucceeded']
assert hashlib.sha256(pathlib.Path('/var/www/recruitmentsaas/RecruitmentSaaS.dll').read_bytes()).hexdigest()==payload['dllSha256']
# Validate credential and model availability without providing any customer data.
request=urllib.request.Request('https://api.openai.com/v1/models',headers={'Authorization':'Bearer '+payload['apiKey']})
try:
 with urllib.request.urlopen(request,timeout=20) as response:
  models=json.load(response)
 assert any(m['id']=='gpt-4.1-mini' for m in models['data'])
except Exception:
 raise SystemExit('Provider credential/model check failed; no pilot activation performed')
envpath=pathlib.Path('/etc/recruitmentsaas/app.env'); old=envpath.read_text(); values={}
for line in old.splitlines():
 if '=' in line and not line.lstrip().startswith('#'):
  k,v=line.split('=',1); values[k.strip()]=v.strip().strip('"').strip("'")
off=['AiSalesDispatch__Enabled','AiSalesDispatch__ReleaseApproved','AiVoice__Enabled','AiVoice__ExternalProcessingApproved','AiChatLearning__Enabled','AiOfficeBooking__Enabled','StagingValidation__Enabled']
assert all(values.get(k)=='false' for k in off)
assert values.get('AiSalesEngine__DraftsEnabled')=='false' and values.get('AiSalesEngine__ExternalProcessingApproved')=='false'
shutil.copy2(envpath,root/'app.env.before-single-draft-pilot'); os.chmod(root/'app.env.before-single-draft-pilot',0o600)
updates={'AiSales__Enabled':'true','AiSalesEngine__DraftsEnabled':'true','AiSalesEngine__ExternalProcessingApproved':'true',
'AiSalesEngine__ApprovedConversationIds__0':payload['chat'],'AiSalesEngine__ApiKey':payload['apiKey'],
'AiSalesEngine__Model':'gpt-4.1-mini','AiSalesEngine__MaxDraftsPerDay':'10','AiSalesEngine__MaxOutputTokens':'800'}
lines=[line for line in old.splitlines() if line.split('=',1)[0] not in updates and not line.startswith('AiSalesEngine__ApprovedConversationIds__')]
temp=envpath.with_name('app.env.single-draft.tmp')
temp.write_text('\n'.join(lines)+'\n# Explicit owner-approved single Sherif conversation; staff-requested drafts only.\n'+'\n'.join(k+'='+v for k,v in updates.items())+'\n')
os.chmod(temp,0o600); os.chown(temp,0,0); os.replace(temp,envpath)
chat,agent,version,av=payload['chat'],payload['agent'],int(payload['version']),int(payload['agentVersion'])
sql=f'''BEGIN;
SET LOCAL lock_timeout='10s';
SELECT pg_advisory_xact_lock(hashtext('single-draft-pilot:{chat}'));
SELECT "Id" FROM demorecruitment."AiSalesAgents" WHERE "Id"='{agent}' FOR UPDATE;
SELECT "ConversationId" FROM demorecruitment."ConversationControls" WHERE "ConversationId"='{chat}' FOR UPDATE;
DO $$ BEGIN
IF NOT EXISTS (SELECT 1 FROM demorecruitment."AiSalesAgents" a
 JOIN demorecruitment."WhatsAppConversations" c ON c."AssignedAiSalesAgentId"=a."Id"
 JOIN demorecruitment."ConversationControls" x ON x."ConversationId"=c."Id"
 JOIN demorecruitment."Leads" l ON l."Id"=c."LeadId"
 WHERE a."Id"='{agent}' AND a."TenantId"='e539d378-c895-4acd-bd5a-d6b88721ca1e' AND a."Version"={av} AND a."Paused" AND a."Mode"=1
 AND c."Id"='{chat}' AND c."WhatsAppAccountId"='f7af14ad-a879-4eb4-a755-2b3602c4e3c5' AND a."WhatsAppAccountId"=c."WhatsAppAccountId"
 AND a."TeamManagerId"='66ac5018-e353-4506-b95b-81ad62474981' AND l."AssignedAiSalesAgentId"=a."Id"
 AND x."TenantId"=a."TenantId" AND x."Controller"=3 AND x."OwnershipVersion"={version})
 OR (SELECT count(*) FROM demorecruitment."AiLeadAssignments" WHERE "AgentId"='{agent}' AND "EndedAt" IS NULL) <> 1
 THEN RAISE EXCEPTION 'Selected paused assignment changed; inspect before activation'; END IF;
END $$;
UPDATE demorecruitment."AiSalesAgents" SET "Paused"=false,"MaxActiveConversations"=1,"Version"="Version"+1 WHERE "Id"='{agent}';
UPDATE demorecruitment."ConversationControls" SET "Controller"=2,"OwnershipVersion"="OwnershipVersion"+1,"LastActorId"=NULL,
 "Reason"='Owner approved single-conversation supervised drafts; customer sending disabled',"UpdatedAt"=now() WHERE "ConversationId"='{chat}';
INSERT INTO demorecruitment."AiControlAudits" ("Id","TenantId","ConversationId","ActorId","Event","Reason","OwnershipVersion","CreatedAt")
VALUES (gen_random_uuid(),'e539d378-c895-4acd-bd5a-d6b88721ca1e','{chat}',NULL,'OwnerApprovedDraftPilot',
'Owner yes go: last 20 messages and reviewed context; phone/email masking; drafts only',{version+1},now());
INSERT INTO demorecruitment."AuditLogs" ("Id","ActorId","ActorType","EventType","EntityType","EntityId","NewValueJson","CreatedAt")
VALUES (gen_random_uuid(),NULL,2,'AiSingleDraftPilotActivated','AiSalesAgent','{agent}',
'{{"source":"owner_explicit_chat_approval","conversationScope":1,"sendingEnabled":false,"crmUserImpersonated":false}}',now());
COMMIT;'''
try:
 subprocess.run(['systemctl','restart','recruitmentsaas'],check=True)
 subprocess.run(['curl','--fail','--silent','--retry','15','--retry-delay','1','--retry-connrefused','http://127.0.0.1:8082/health'],capture_output=True,check=True)
 pid=subprocess.check_output(['systemctl','show','recruitmentsaas','--property=MainPID','--value'],text=True).strip()
 effective=dict(x.decode().split('=',1) for x in pathlib.Path('/proc/'+pid+'/environ').read_bytes().split(b'\0') if b'=' in x)
 assert all(effective.get(k)=='false' for k in off)
 assert effective.get('AiSalesEngine__DraftsEnabled')=='true' and effective.get('AiSalesEngine__ExternalProcessingApproved')=='true'
 assert effective.get('AiSalesEngine__ApprovedConversationIds__0')==chat
 result=subprocess.run(['runuser','-u','postgres','--','psql','-X','-q','-A','-t','-v','ON_ERROR_STOP=1','-d','RecruitmentCRM'],input=sql,text=True,capture_output=True)
 if result.returncode:
  (root/'activation-failure.log').write_text(result.stderr)
  raise RuntimeError('Scoped activation transaction failed')
except Exception:
 shutil.copy2(root/'app.env.before-single-draft-pilot',envpath)
 subprocess.run(['systemctl','restart','recruitmentsaas'],check=True)
 raise SystemExit('Activation failed; prior configuration restored; inspect private recovery logs')
state={'singleConversationDraftPilotEnabled':True,'conversationId':chat,'agentId':agent,'ownershipVersion':version+1,
'model':'gpt-4.1-mini','maxRecentMessages':20,'maxDraftsPerDay':10,'maxOutputTokens':800,'customerSendingEnabled':False,
'voiceEnabled':False,'crmUserImpersonated':False,'providerCredentialValidated':True,'draftGeneratedByThisTool':False}
(root/'single-draft-pilot-result.json').write_text(json.dumps(state,indent=2)); os.chmod(root/'single-draft-pilot-result.json',0o600)
print(json.dumps(state))
"""
script = root/'activate-remote.py'
script.write_text(remote,encoding='utf-8')
ssh=['ssh','-o','BatchMode=yes','-o','StrictHostKeyChecking=yes','-o','ConnectTimeout=20','ubuntu@158.69.206.138']
remote_path=payload['root']+'/activate-remote.py'
uploaded=subprocess.run(['scp','-O','-q','-o','BatchMode=yes','-o','StrictHostKeyChecking=yes','-o','ConnectTimeout=20',str(script),'ubuntu@158.69.206.138:'+remote_path],capture_output=True)
if uploaded.returncode:
    raise SystemExit('Activation script transfer failed; no activation performed')
result=subprocess.run(ssh+['sudo -n python3 '+remote_path],input=json.dumps(payload),text=True,encoding='utf-8',capture_output=True)
if result.returncode:
    # No exception payloads, request headers or key material are printed.
    raise SystemExit('Activation did not report success; inspect private state before retrying')
state=json.loads(result.stdout)
(scope_root/'sherif-single-draft-pilot-result.json').write_text(json.dumps(state,indent=2),encoding='utf-8')
print(json.dumps(state))
