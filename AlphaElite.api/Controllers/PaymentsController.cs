using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AlphaElite.domain.entities;
using AlphaElite.infrastructure.services;

namespace AlphaElite.api.Controllers
{
    [ApiController]
    [Route("api/tenant/{companyId:int}/[controller]")]
    public class PaymentsController : ControllerBase
    {
        private readonly ITenantDbContextFactory _tenantFactory;

        public PaymentsController(ITenantDbContextFactory tenantFactory)
        {
            _tenantFactory = tenantFactory;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(int companyId)
        {
            await using var tenantDb = await _tenantFactory.CreateAsync(companyId);
            var list = await tenantDb.Payments.AsNoTracking().OrderByDescending(p => p.PaymentDate).ToListAsync();
            return Ok(list);
        }

        [HttpGet("{id:int}", Name = "GetPayment")]
        public async Task<IActionResult> GetById(int companyId, int id)
        {
            await using var tenantDb = await _tenantFactory.CreateAsync(companyId);
            var item = await tenantDb.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.PaymentId == id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [HttpPost]
        public async Task<IActionResult> Create(int companyId, [FromBody] Payment payment)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            await using var tenantDb = await _tenantFactory.CreateAsync(companyId);
            tenantDb.Payments.Add(payment);
            await tenantDb.SaveChangesAsync();

            return CreatedAtRoute("GetPayment", new { companyId, id = payment.PaymentId }, payment);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int companyId, int id, [FromBody] Payment payment)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (id != payment.PaymentId) return BadRequest();

            await using var tenantDb = await _tenantFactory.CreateAsync(companyId);
            var existing = await tenantDb.Payments.FindAsync(id);
            if (existing == null) return NotFound();

            existing.AmountPaid = payment.AmountPaid;
            existing.PaymentDate = payment.PaymentDate;
            existing.NextDueDate = payment.NextDueDate;
            existing.Notes = payment.Notes;

            await tenantDb.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int companyId, int id)
        {
            await using var tenantDb = await _tenantFactory.CreateAsync(companyId);
            var existing = await tenantDb.Payments.FindAsync(id);
            if (existing == null) return NotFound();

            tenantDb.Payments.Remove(existing);
            await tenantDb.SaveChangesAsync();

            return NoContent();
        }
    }
}
