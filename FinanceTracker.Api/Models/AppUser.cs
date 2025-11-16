using System.ComponentModel.DataAnnotations;

namespace FinanceTracker.Api.Models
{
    public class AppUser
    {
        public int Id { get; set; }

        [Required]
        public string Username { get; set; } = null!;

        [Required]
        public string PasswordHash { get; set; } = null!;
    }
}
