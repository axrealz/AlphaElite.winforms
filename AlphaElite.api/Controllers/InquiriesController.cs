using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AlphaElite.domain.entities;
using AlphaElite.infrastructure.services;

namespace AlphaElite.api.Controllers
{
    [ApiController]
    [Route("api/tenant/{companyId:int}/[controller]")]
    public class InquiriesController : ControllerBase
    {
        private readonly ITenantDbContextFactory _tenantFactory;

        public InquiriesController(ITenantDbContextFactory tenantFactory)
        {
            _tenantFactory = tenantFactory;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(int companyId)
        {
            await using var tenantDb = await _tenantFactory.CreateAsync(companyId);
            var list = await tenantDb.Inquiries.AsNoTracking().OrderByDescending(i => i.CreatedAt).ToListAsync();
            return Ok(list);
        }

        [HttpGet("{id:int}", Name = "GetInquiry")]
        public async Task<IActionResult> GetById(int companyId, int id)
        {
            await using var tenantDb = await _tenantFactory.CreateAsync(companyId);
            var item = await tenantDb.Inquiries.AsNoTracking().FirstOrDefaultAsync(i => i.InquiryId == id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [HttpPost]
        public async Task<IActionResult> Create(int companyId, [FromBody] Inquiry inquiry)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            await using var tenantDb = await _tenantFactory.CreateAsync(companyId);
            tenantDb.Inquiries.Add(inquiry);
            await tenantDb.SaveChangesAsync();

            return CreatedAtRoute("GetInquiry", new { companyId, id = inquiry.InquiryId }, inquiry);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int companyId, int id)
        {
            await using var tenantDb = await _tenantFactory.CreateAsync(companyId);
            var existing = await tenantDb.Inquiries.FindAsync(id);
            if (existing == null) return NotFound();

            tenantDb.Inquiries.Remove(existing);
            await tenantDb.SaveChangesAsync();

            return NoContent();
        }
    }
}
