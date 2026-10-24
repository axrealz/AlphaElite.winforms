using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AlphaElite.domain.entities;
using AlphaElite.infrastructure.services;

namespace AlphaElite.api.Controllers
{
    [ApiController]
    [Route("api/tenant/{companyId:int}/[controller]")]
    public class MembersController : ControllerBase
    {
        private readonly ITenantDbContextFactory _tenantFactory;

        public MembersController(ITenantDbContextFactory tenantFactory)
        {
            _tenantFactory = tenantFactory;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(int companyId)
        {
            await using var tenantDb = await _tenantFactory.CreateAsync(companyId);
            var list = await tenantDb.Members.AsNoTracking().OrderBy(m => m.MemberId).ToListAsync();
            return Ok(list);
        }

        [HttpGet("{id:int}", Name = "GetMember")]
        public async Task<IActionResult> GetById(int companyId, int id)
        {
            await using var tenantDb = await _tenantFactory.CreateAsync(companyId);
            var member = await tenantDb.Members.AsNoTracking().FirstOrDefaultAsync(m => m.MemberId == id);
            if (member == null) return NotFound();
            return Ok(member);
        }

        [HttpPost]
        public async Task<IActionResult> Create(int companyId, [FromBody] Member member)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            await using var tenantDb = await _tenantFactory.CreateAsync(companyId);
            tenantDb.Members.Add(member);
            await tenantDb.SaveChangesAsync();

            return CreatedAtRoute("GetMember", new { companyId, id = member.MemberId }, member);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int companyId, int id, [FromBody] Member member)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (id != member.MemberId) return BadRequest();

            await using var tenantDb = await _tenantFactory.CreateAsync(companyId);

            var existing = await tenantDb.Members.FindAsync(id);
            if (existing == null) return NotFound();

            // Update allowed fields
            existing.MemberCode = member.MemberCode;
            existing.FullName = member.FullName;
            existing.Email = member.Email;
            existing.IsActive = member.IsActive;

            await tenantDb.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int companyId, int id)
        {
            await using var tenantDb = await _tenantFactory.CreateAsync(companyId);
            var existing = await tenantDb.Members.FindAsync(id);
            if (existing == null) return NotFound();

            tenantDb.Members.Remove(existing);
            await tenantDb.SaveChangesAsync();

            return NoContent();
        }
    }
}
