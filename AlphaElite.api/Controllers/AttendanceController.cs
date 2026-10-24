using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AlphaElite.domain.entities;
using AlphaElite.infrastructure.services;

namespace AlphaElite.api.Controllers
{
    [ApiController]
    [Route("api/tenant/{companyId:int}/[controller]")]
    public class AttendanceController : ControllerBase
    {
        private readonly ITenantDbContextFactory _tenantFactory;

        public AttendanceController(ITenantDbContextFactory tenantFactory)
        {
            _tenantFactory = tenantFactory;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(int companyId)
        {
            await using var tenantDb = await _tenantFactory.CreateAsync(companyId);
            var list = await tenantDb.Attendances.AsNoTracking().OrderByDescending(a => a.CheckInDate).ToListAsync();
            return Ok(list);
        }

        [HttpGet("{id:int}", Name = "GetAttendance")]
        public async Task<IActionResult> GetById(int companyId, int id)
        {
            await using var tenantDb = await _tenantFactory.CreateAsync(companyId);
            var item = await tenantDb.Attendances.AsNoTracking().FirstOrDefaultAsync(a => a.AttendanceId == id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [HttpPost]
        public async Task<IActionResult> Create(int companyId, [FromBody] Attendance attendance)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            await using var tenantDb = await _tenantFactory.CreateAsync(companyId);
            tenantDb.Attendances.Add(attendance);
            await tenantDb.SaveChangesAsync();

            return CreatedAtRoute("GetAttendance", new { companyId, id = attendance.AttendanceId }, attendance);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int companyId, int id)
        {
            await using var tenantDb = await _tenantFactory.CreateAsync(companyId);
            var existing = await tenantDb.Attendances.FindAsync(id);
            if (existing == null) return NotFound();

            tenantDb.Attendances.Remove(existing);
            await tenantDb.SaveChangesAsync();

            return NoContent();
        }
    }
}
