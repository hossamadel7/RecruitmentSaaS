"""Run privately on the server after an owner-approved release installation.

Enables explicit staff-reviewed sending for two selected conversations. Never
invokes a model or customer transport. Credentials remain on the server.
"""
import hashlib
import json
import os
import pathlib
import shutil
import subprocess
import sys
import tarfile

root = pathlib.Path(sys.argv[1]).resolve()
assert str(root).startswith('/home/ubuntu/.local/share/recruitmentsaas-installations/')
assert os.geteuid() == 0
assert json.loads((root / 'result.json').read_text())['installationSucceeded']
assert hashlib.sha256(pathlib.Path('/var/www/recruitmentsaas/RecruitmentSaaS.dll').read_bytes()).hexdigest() == sys.argv[2].lower()
assert hashlib.sha256((root / 'owner-runner.tar.gz').read_bytes()).hexdigest() == sys.argv[3].lower()
assert not (root / 'reviewed-activation.json').exists(), 'Already activated; inspect instead of replaying'
runner = root / 'owner-runner'
runner.mkdir(exist_ok=True, mode=0o700)
with tarfile.open(root / 'owner-runner.tar.gz') as archive:
    for member in archive.getmembers():
        assert member.isfile() and (runner / member.name).resolve().is_relative_to(runner)
    archive.extractall(runner)

def query(sql):
    result = subprocess.run(['runuser', '-u', 'postgres', '--', 'psql', '-X', '-qAt', '-v', 'ON_ERROR_STOP=1', '-d', 'RecruitmentCRM'], input=sql, text=True, capture_output=True)
    if result.returncode:
        (root / 'reviewed-query-error.log').write_text(result.stderr)
        raise RuntimeError('Database check failed; inspect private log')
    return result.stdout.strip()

old_chat = '30c63932-39c4-4477-83cb-4e074556328f'
new_chat = 'f9d372fd-a4f8-4ec6-8c8d-ad3a2d095639'
bakar_chat = 'f5ada282-3778-485a-a3d8-984d1d869c05'
sherif_agent = '27039db3-1dba-4425-94a3-c1bc8fd5a72a'
tenant = 'e539d378-c895-4acd-bd5a-d6b88721ca1e'
prior = (root / 'app.env.before').read_text()
values = {}
for line in prior.splitlines():
    if '=' in line and not line.lstrip().startswith('#'):
        k, v = line.split('=', 1)
        values[k.strip()] = v.strip().strip('"').strip("'")
assert values.get('AiSalesEngine__DraftsEnabled') == 'true'
assert values.get('AiSalesEngine__ExternalProcessingApproved') == 'true'
assert values.get('AiSalesDispatch__Enabled') == 'false'
assert {v for k, v in values.items() if k.startswith('AiSalesEngine__ApprovedConversationIds__')} == {old_chat, bakar_chat}
connection = values.get('ConnectionStrings__DefaultConnection')
if not connection:
    config = {}
    for filename in ['appsettings.json', 'appsettings.Production.json']:
        p = pathlib.Path('/var/www/recruitmentsaas') / filename
        if p.exists():
            config.update(json.loads(p.read_text()).get('ConnectionStrings', {}))
    connection = config['DefaultConnection']
before_count = int(query('SELECT count(*) FROM demorecruitment."AiOutboundIntents";'))
assert before_count == 0, 'Inspect existing dispatch records before activation'
old = json.loads(query(f'''SELECT json_build_object('lead',c."LeadId",'agent',c."AssignedAiSalesAgentId",'version',x."OwnershipVersion") FROM demorecruitment."WhatsAppConversations" c JOIN demorecruitment."ConversationControls" x ON x."ConversationId"=c."Id" WHERE c."Id"='{old_chat}';'''))
assert old['agent'] == sherif_agent
assert query(f'''SELECT count(*) FROM demorecruitment."AiSalesAgents" WHERE "Id" IN ('{sherif_agent}','ccf48b57-3987-516e-b0f9-9c1876e5bdb2') AND NOT "Paused";''') == '2'

def control(command, chat, lead, version):
    request = dict(ConnectionString=connection, TenantId=tenant, LeadId=lead,
                   ConversationId=chat, AgentId=sherif_agent, OwnerApproved=True,
                   Command=command, ExpectedVersion=version,
                   Key='owner-reviewed-pilot-20261015-' + command,
                   SourceReference='Owner explicitly selected Sherif LD-04075 replacing LD-04043 and approved reviewed sending for it and Bakar LD-03277 in chat.')
    env = dict(os.environ, DOTNET_ROLL_FORWARD='LatestMajor')
    result = subprocess.run(['dotnet', str(runner / 'OwnerPilotControl.dll')], input=json.dumps(request), text=True, capture_output=True, env=env, cwd=runner)
    (root / ('control-' + command + '.log')).write_text(result.stdout + result.stderr)
    assert result.returncode == 0, 'Control failed; inspect private log before retrying'
    return json.loads(result.stdout.strip())

released = control('release', old_chat, old['lead'], old['version'])
assigned = control('assign', new_chat, 'a7fe1b9d-f341-473c-8c1b-936eae5a988f', 0)
updates = {'AiSales__Enabled': 'true', 'AiSalesEngine__DraftsEnabled': 'true',
           'AiSalesEngine__ExternalProcessingApproved': 'true',
           'AiSalesDispatch__Enabled': 'true', 'AiSalesDispatch__ReleaseApproved': 'true',
           'AiSalesDispatch__MaxAttemptsPerDay': '10',
           'AiSalesDispatch__ApprovedAccountIds__0': 'f7af14ad-a879-4eb4-a755-2b3602c4e3c5',
           'AiSalesDispatch__ApprovedAccountIds__1': '48af32a2-e7ce-440f-8a10-9b4f5d0dd6ad'}
for prefix in ['AiSalesEngine__ApprovedConversationIds__', 'AiSalesDispatch__ApprovedConversationIds__']:
    updates[prefix + '0'] = new_chat
    updates[prefix + '1'] = bakar_chat
off = ['AiVoice__Enabled', 'AiVoice__ExternalProcessingApproved', 'AiChatLearning__Enabled', 'AiOfficeBooking__Enabled', 'StagingValidation__Enabled']
updates.update({k: 'false' for k in off})
prefixes = ('AiSalesEngine__ApprovedConversationIds__', 'AiSalesDispatch__ApprovedConversationIds__', 'AiSalesDispatch__ApprovedAccountIds__')
lines = [line for line in prior.splitlines() if line.split('=', 1)[0].strip() not in updates and not line.strip().startswith(prefixes)]
envpath = pathlib.Path('/etc/recruitmentsaas/app.env')
shutil.copy2(envpath, root / 'app.env.before-reviewed-activation')
temp = envpath.with_name('app.env.reviewed-tmp')
temp.write_text('\n'.join(lines) + '\n# Owner-approved explicit reviewed sending for two conversations.\n' + '\n'.join(k + '=' + v for k, v in updates.items()) + '\n')
os.chmod(temp, 0o600)
os.replace(temp, envpath)
try:
    subprocess.run(['systemctl', 'restart', 'recruitmentsaas'], check=True)
    subprocess.run(['curl', '--fail', '--silent', '--retry', '15', '--retry-delay', '1', '--retry-connrefused', 'http://127.0.0.1:8082/health'], capture_output=True, check=True)
    pid = subprocess.check_output(['systemctl', 'show', 'recruitmentsaas', '--property=MainPID', '--value'], text=True).strip()
    effective = dict(x.split('=', 1) for x in pathlib.Path('/proc/' + pid + '/environ').read_bytes().decode().split('\0') if '=' in x)
    assert all(effective.get(k) == v for k, v in updates.items())
    assert {v for k, v in effective.items() if k.startswith('AiSalesDispatch__ApprovedConversationIds__')} == {new_chat, bakar_chat}
    assert query(f'''SELECT count(*) FROM demorecruitment."ConversationControls" WHERE "ConversationId" IN ('{new_chat}','{bakar_chat}') AND "Controller"=2;''') == '2'
    assert query(f'''SELECT count(*) FROM demorecruitment."WhatsAppConversations" WHERE "Id"='{old_chat}' AND "AssignedAiSalesAgentId" IS NULL;''') == '1'
    after_count = int(query('SELECT count(*) FROM demorecruitment."AiOutboundIntents";'))
    result = dict(enabled=True, selectedConversations=2, oldSherifReleased=True,
                  release= released, assignment=assigned,
                  outboundBefore=before_count, outboundAfter=after_count,
                  sendsInvokedByActivation=0, userImpersonated=False)
    (root / 'reviewed-activation.json').write_text(json.dumps(result, indent=2))
    print(json.dumps(result))
except Exception:
    shutil.copy2(root / 'app.env.before-reviewed-activation', envpath)
    subprocess.run(['systemctl', 'restart', 'recruitmentsaas'], check=True)
    raise SystemExit('Activation verification failed; dispatch disabled. Inspect assignments before retrying.')
