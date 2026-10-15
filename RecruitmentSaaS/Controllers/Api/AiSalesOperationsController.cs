using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecruitmentSaaS.Services;

namespace RecruitmentSaaS.Controllers.Api;
[ApiController, Route("api/ai-sales-operations"), Authorize(Roles = "1")]
public sealed class AiSalesOperationsController(AiSalesOperationsService operations) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        try { return Ok(await operations.ReadAsync(ct)); }
        catch (Npgsql.NpgsqlException) { return StatusCode(503, new { error = "Operational data unavailable; check database connectivity and migration status." }); }
    }
}
