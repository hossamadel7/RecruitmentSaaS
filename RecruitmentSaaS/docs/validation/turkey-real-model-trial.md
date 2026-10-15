# Turkey real-model trial — 13 October 2026

The owner authorized use of an API key for a local trial. The key authenticated through the models endpoint and GPT-4.1 mini was available. The credential is stored using Windows DPAPI under the current user's LocalAppData, outside Git. It is not installed on the production server. Rotate the key exposed in chat after the trial.

`Tests/RealModelRunner` builds successfully with no warnings or errors. It directly calls the existing `OpenAiSalesDraftProvider`; it does not start the web app, database, workers or WhatsApp transport. Only owner-confirmed business knowledge and fictional applicant questions are supplied. The runner accepts 1–20 scenarios, processes them sequentially, stops at the first provider failure, and never retries. Reports stay outside the repository. Fact-reference validation is structural; semantic scores require human review.

The first of 18 draft requests stopped with `ProviderHttp429`. **Zero drafts were generated.** One separate minimal diagnostic request also returned HTTP 429; its specific quota/rate-limit code could not be established. No automated retry loop was started. Authentication success alone does not establish available billing credit or generation quota.

The private report is under `%LOCALAPPDATA%\RecruitmentSaaS\AiSalesTrials\2026-10-13-first-real-model`. It records the prompt version, knowledge/scenario hashes, attempted/completed counts and failure. No customer chat export was uploaded, customer reply sent, live configuration changed or model fine-tuned.

After the owner asked to continue, a fresh run succeeded: 18/18 responses were generated. The initial run frequently misunderstood the escaped Arabic context. Changing the model-visible nested JSON to readable Arabic improved the responses. A subsequent review found Playbook citations, so the strict response schema now permits only current Fact IDs (or no citations if there are no facts). The revised knowledge wording separates passport-holder documents from the no-passport visit exception.

The latest `egyptian-sales-draft-v3-fact-citations` run generated **18/18 drafts with 18/18 valid Fact references**. This is citation validity, not 18 semantic passes. The run reported 50,968 input tokens and 3,862 output tokens; these are final-run usage only, not the total of all trials or a billing amount. Its report and reply worksheet are under `%LOCALAPPDATA%\RecruitmentSaaS\AiSalesTrials\2026-10-13-final-facts-27756182-97bd-4a68-bb83-8e476e68b775`.

Agent review flagged TR04 (overstates company-wide Turkey-only scope), TR13 (unconfirmed other opportunities), TR14 (inaccurate unsolicited document summary), TR15 (stop intent with needsHuman=false and continuing invitation), and TR16 (diverts staff request into office visit). The CRM's existing local request/opt-out policy bypasses the provider, and its InvalidHandoff check rejects inconsistent handoff flags; this provider-only trial intentionally exercises the model without those paths. Human scores remain unset. **No live release is qualified.** Owner review of the actual replies is next.

Three provider contract tests passed, including readable Arabic round-trip, strict schema/storage settings and exclusion of Playbook IDs from allowed citations. The trial runner also builds successfully. No production code was deployed or configuration enabled.

For a repeat trial, load the encrypted credential only into the child process environment (never a command-line argument):

```powershell
$ErrorActionPreference = 'Stop'
$secretPath = Join-Path $env:LOCALAPPDATA 'RecruitmentSaaS\Secrets\openai-api-key.dpapi'
$secureKey = (Get-Content -Raw -LiteralPath $secretPath).Trim() | ConvertTo-SecureString
$credential = [System.Management.Automation.PSCredential]::new('api', $secureKey)
$trialOutput = Join-Path $env:LOCALAPPDATA ('RecruitmentSaaS\AiSalesTrials\' + [guid]::NewGuid())
dotnet build Tests\RealModelRunner\RealModelRunner.csproj --nologo
if ($LASTEXITCODE -ne 0) { throw 'Trial build failed.' }
try {
    $env:AI_SALES_TRIAL_API_KEY = $credential.GetNetworkCredential().Password
    dotnet Tests\RealModelRunner\bin\Debug\net8.0\RealModelRunner.dll (Get-Location).Path $trialOutput 'gpt-4.1-mini'
} finally {
    Remove-Item Env:\AI_SALES_TRIAL_API_KEY -ErrorAction SilentlyContinue
    $credential = $null
    $secureKey = $null
}
```

This bypasses the CRM's approval/database path deliberately for a bounded provider-only trial; it is not evidence that the deployed CRM is configured or ready to send.
