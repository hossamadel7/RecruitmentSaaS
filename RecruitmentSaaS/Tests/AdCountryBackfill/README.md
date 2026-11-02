# Repair historical ad-country labels

One-time SQL generator for chats captured before ad-country persistence was introduced.
Uses the production `CountryRouting.Detect` implementation and only fills null
`WhatsAppConversations.AdCountry` values. It does not change leads, ages, owners,
statuses, team memberships, or routing settings. Unknown and ambiguous ads remain unknown.

Export an array of `{ "Id": "conversation-guid", "Headline": "...", "Body": "..." }`
from the latest saved referral for conversations whose effective country is null.
Keep exports outside the repository; never include customer messages or contact details.

```powershell
dotnet run --project Tests/AdCountryBackfill -- input.json output.sql
```

Review the aggregate summary and SQL, take a database backup, then apply the SQL
using the existing production database maintenance process. Updates are guarded by
`AdCountry IS NULL` and wrapped in a transaction, so rerunning the repair is safe.
