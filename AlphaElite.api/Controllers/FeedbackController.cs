using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AlphaElite.domain.entities;
using AlphaElite.infrastructure.services;

namespace AlphaElite.api.Controllers
{
    [ApiController]
    [Route("api/tenant/{companyId:int}/[controller]")]
    public class FeedbackController : ControllerBase
    {
        private readonly ITenantDbContextFactory _tenantFactory;

        public FeedbackController(ITenantDbContextFactory tenantFactory)
        {
            _tenantFactory = tenantFactory;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(int companyId)
        {
            await using var tenantDb = await _tenantFactory.CreateAsync(companyId);
            var list = await tenantDb.Feedbacks.AsNoTracking().OrderByDescending(f => f.SubmittedAt).ToListAsync();
            return Ok(list);
        }

        [HttpGet("{id:int}", Name = "GetFeedback")]
        public async Task<IActionResult> GetById(int companyId, int id)
        {
            await using var tenantDb = await _tenantFactory.CreateAsync(companyId);
            var item = await tenantDb.Feedbacks.AsNoTracking().FirstOrDefaultAsync(f => f.FeedbackId == id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [HttpPost]
        public async Task<IActionResult> Create(int companyId, [FromBody] Feedback feedback)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            await using var tenantDb = await _tenantFactory.CreateAsync(companyId);
            tenantDb.Feedbacks.Add(feedback);
            await tenantDb.SaveChangesAsync();

            return CreatedAtRoute("GetFeedback", new { companyId, id = feedback.FeedbackId }, feedback);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int companyId, int id)
        {
            await using var tenantDb = await _tenantFactory.CreateAsync(companyId);
            var existing = await tenantDb.Feedbacks.FindAsync(id);
            if (existing == null) return NotFound();

            tenantDb.Feedbacks.Remove(existing);
            await tenantDb.SaveChangesAsync();

            return NoContent();
        }
    }
}
