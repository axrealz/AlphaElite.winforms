using System;
using System.Collections.Generic;
using System.Text;

namespace AlphaElite.domain.entities
{
    public class Attendance
    {
        public int AttendanceId { get; set; }
        public int MemberId { get; set; }
        public DateTime CheckInDate { get; set; } = DateTime.UtcNow;
        public Member? Member { get; set; }
        public int? BranchId { get; set; }
    }
}
