using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RTROPToLogoIntegration.Domain.Entities
{
    /// <summary>
    /// MRP hesaplama parametrelerini firma bazlı saklayan tablo.
    /// İlk gönderimde INSERT, sonraki gönderimlerde UPDATE yapılır.
    /// </summary>
    [Table("MRP_ITEM_PARAMETERS")]
    public class MrpItemParameter
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [MaxLength(25)]
        public string FirmNo { get; set; }

        [Required]
        [MaxLength(25)]
        public string ItemID { get; set; }

        [MaxLength(10)]
        public string? ABCDClassification { get; set; }

        [MaxLength(10)]
        public string? PlanningType { get; set; }

        public double SafetyStock { get; set; }
        public double ROP { get; set; }
        public double Max { get; set; }
        public double OrderQuantity { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
