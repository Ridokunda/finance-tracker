using System;
using System.ComponentModel.DataAnnotations;

namespace FinanceTracker.Api.Models
{
    public class RevokedToken
    {
        public int Id { get; set; }

        [Required]
        public string Token { get; set; }

        public DateTime ExpiresAt { get; set; }
    }
}
