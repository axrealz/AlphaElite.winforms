using System;
using System.Collections.Generic;
using System.Text;

namespace AlphaElite.domain.entities
{
    public class Member
    {
        public int MemberId { get; set; }
        public string MemberCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int? BranchId { get; set; }
    }
}
