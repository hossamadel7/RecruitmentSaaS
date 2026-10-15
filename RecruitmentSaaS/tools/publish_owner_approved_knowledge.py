"""Publish the explicitly owner-approved company pack, without CRM impersonation.

Requires deployed AiOwnerRecordedKnowledgeApproval migration, a paused agent,
the private confirmed Bakar scope, and all AI gates disabled. This is not a
general chat-learning importer and does not upload customer chats.
"""
import datetime
import hashlib
import json
import pathlib
import subprocess
import sys
import uuid

scope_path = pathlib.Path(sys.argv[1])
scope = json.loads(scope_path.read_text(encoding='utf-8-sig'))
pack_path = pathlib.Path('docs/knowledge/turkey-owner-knowledge.json')
pack = json.loads(pack_path.read_text(encoding='utf-8-sig'))
assert pack['packId'] == 'alfahd-turkey-owner-v2'
assert pack['humanApproval']['status'] == 'approved_in_owner_chat'
assert not pack['humanApproval']['duplicateApprovalRequired']
assert scope['accountName'] in ('Bakar', 'Sherif') and scope['branchConfirmed']
tenant = str(uuid.UUID(scope['reservedTenantId']))
account = str(uuid.UUID(scope['accountId']))
branch = str(uuid.UUID(scope['currentBranchId']))
agent = str(uuid.UUID(scope['agentSetup']['agentId']))
entries = pack['entries']
assert len(entries) == 8 and sum(e['kind'] == 0 for e in entries) == 6
assert len({e['key'] for e in entries}) == 8
approval = 'CEO replies supplied by owner and explicitly confirmed as facts in this chat; duplicate approval waived by owner on 2026-10-14'
digest = hashlib.sha256(pack_path.read_bytes()).hexdigest()

def literal(value):
    return "'" + str(value).replace("'", "''") + "'"

statements = ['BEGIN;', "SET LOCAL lock_timeout='10s';", "SET LOCAL statement_timeout='120s';",
    f"SELECT pg_advisory_xact_lock(hashtext('knowledge-account:{tenant}:{account}'));",
    f'''DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM demorecruitment."AiSalesAgents"
    WHERE "Id"='{agent}' AND "TenantId"='{tenant}' AND "WhatsAppAccountId"='{account}' AND "Paused" AND "Mode"=1)
    THEN RAISE EXCEPTION 'Paused Bakar agent required'; END IF; END $$;''']
ids = []
for e in entries:
    assert e['kind'] in (0,1) and len(e['title']) <= 150 and len(e['content']) <= 4000
    identifier = str(uuid.uuid5(uuid.UUID(tenant), ('' if scope['accountName'] == 'Bakar' else account + ':') + pack['packId'] + ':' + e['key']))
    ids.append(identifier)
    source = e['sourceReference'] + '; pack=' + pack['packId'] + '; key=' + e['key']
    assert len(source) <= 500
    # Fail closed on a prior entry with different facts or attribution; never overwrite it.
    statements.append(f'''DO $$ BEGIN IF EXISTS (SELECT 1 FROM demorecruitment."AiSalesKnowledge" WHERE "Id"='{identifier}'
    AND NOT ("TenantId"='{tenant}' AND "WhatsAppAccountId"='{account}' AND "Content"::text={literal(e['content'])}
    AND "Title"::text={literal(e['title'])} AND "Kind"={e['kind']} AND "Status"=1 AND "CreatedById" IS NULL
    AND "ApprovedById" IS NULL AND "OwnerApprovalReference"::text={literal(approval)} AND "ValidUntil">now()))
    THEN RAISE EXCEPTION 'Existing knowledge differs or expired'; END IF; END $$;''')
    audit_json = json.dumps({'source': 'owner_authorized_CEO_knowledge_publication', 'packId': pack['packId'],
        'packSha256': digest, 'key': e['key'], 'officeBranchId': branch,
        'approvalReference': approval, 'crmUserImpersonated': False}, ensure_ascii=False)
    statements.append(f'''WITH inserted AS (INSERT INTO demorecruitment."AiSalesKnowledge"
    ("Id","TenantId","WhatsAppAccountId","Kind","Status","Title","Content","SourceReference",
     "ValidFrom","ValidUntil","CreatedById","ApprovedById","ApprovedAt","OwnerApprovalReference","Version","CreatedAt")
    VALUES ('{identifier}','{tenant}','{account}',{e['kind']},1,{literal(e['title'])},{literal(e['content'])},{literal(source)},
    now(),now()+interval '7 days',NULL,NULL,now(),{literal(approval)},1,now()) ON CONFLICT ("Id") DO NOTHING RETURNING "Id")
    INSERT INTO demorecruitment."AuditLogs" ("Id","ActorId","ActorType","EventType","EntityType","EntityId","NewValueJson","CreatedAt")
    SELECT gen_random_uuid(),NULL,2,'AiOwnerKnowledgePublished','AiSalesKnowledge',"Id",{literal(audit_json)},now() FROM inserted;''')
statements.append('COMMIT;')
selected_ids = ','.join(literal(i) for i in ids)
query = f'''SELECT json_build_object('entries',count(*),'facts',count(*) FILTER (WHERE "Kind"=0),
'playbooks',count(*) FILTER (WHERE "Kind"=1),'approved',bool_and("Status"=1),'userImpersonated',bool_or("CreatedById" IS NOT NULL OR "ApprovedById" IS NOT NULL),
'validFrom',min("ValidFrom"),'validUntil',min("ValidUntil")) FROM demorecruitment."AiSalesKnowledge" WHERE "Id" IN ({selected_ids});'''
payload = {'sql':'\n'.join(statements), 'query':query, 'tenant':tenant, 'packSha256':digest}
remote = r'''
import json,pathlib,subprocess,datetime,uuid,os
payload=PAYLOAD
pid=subprocess.check_output(['systemctl','show','recruitmentsaas','--property=MainPID','--value'],text=True).strip()
values=dict(x.decode().split('=',1) for x in pathlib.Path('/proc/'+pid+'/environ').read_bytes().split(b'\0') if b'=' in x)
gates=['AiSales__Enabled','AiSalesEngine__DraftsEnabled','AiSalesEngine__ExternalProcessingApproved','AiSalesDispatch__Enabled','AiSalesDispatch__ReleaseApproved','AiVoice__Enabled','AiVoice__ExternalProcessingApproved','AiChatLearning__Enabled','AiOfficeBooking__Enabled']
assert all(values.get(k)=='false' for k in gates), 'AI gates must remain disabled'
assert values.get('AiSales__TenantId')==payload['tenant'], 'Tenant mismatch'
root=pathlib.Path('/home/ubuntu/.local/share/recruitmentsaas-knowledge-publication')/(datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%d')+'-'+uuid.uuid4().hex)
root.mkdir(parents=True,mode=0o700); os.chmod(root,0o700)
with (root/'knowledge-before.sql').open('wb') as f:
 subprocess.run(['runuser','-u','postgres','--','pg_dump','-d','RecruitmentCRM','--data-only','--table=demorecruitment."AiSalesKnowledge"'],stdout=f,check=True)
os.chmod(root/'knowledge-before.sql',0o600)
psql=['runuser','-u','postgres','--','psql','-X','-q','-A','-t','-v','ON_ERROR_STOP=1','-d','RecruitmentCRM']
result=subprocess.run(psql,input=payload['sql'],text=True,capture_output=True)
if result.returncode:
 (root/'failure.log').write_text(result.stderr); raise SystemExit('Knowledge transaction failed; private log retained')
result=subprocess.run(psql,input=payload['query'],text=True,capture_output=True,check=True)
state=json.loads(result.stdout)
assert state['entries']==8 and state['facts']==6 and state['playbooks']==2 and state['approved'] and not state['userImpersonated']
state.update({'packSha256':payload['packSha256'],'allAiGatesDisabled':True,'recoveryDirectory':str(root)})
(root/'result.json').write_text(json.dumps(state,indent=2)); os.chmod(root/'result.json',0o600)
print(json.dumps(state))
'''.replace('PAYLOAD',repr(payload))
result = subprocess.run(['ssh','-o','BatchMode=yes','-o','StrictHostKeyChecking=yes','-o','ConnectTimeout=20',
    'ubuntu@158.69.206.138','sudo -n python3 -'], input=remote, text=True, encoding='utf-8', capture_output=True)
if result.returncode:
    raise SystemExit(result.stderr[-2000:])
state=json.loads(result.stdout)
(scope_path.parent/('knowledge-publication-result.json' if scope['accountName']=='Bakar' else scope['accountName'].lower()+'-knowledge-publication-result.json')).write_text(json.dumps(state,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in state.items() if k!='recoveryDirectory'}))
