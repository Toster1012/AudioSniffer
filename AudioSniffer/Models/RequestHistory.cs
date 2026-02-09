using System;
using System.ComponentModel.DataAnnotations;

namespace AudioSniffer.Models
{
    public class RequestHistory
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(255)]
        public string FileName { get; set; } = string.Empty;

        [Required]
        public bool IsGenerated { get; set; }

        [Required]
        public DateTime RequestDate { get; set; } = DateTime.UtcNow;
    }
}