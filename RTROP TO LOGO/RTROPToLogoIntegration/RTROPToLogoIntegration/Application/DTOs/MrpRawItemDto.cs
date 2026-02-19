using System.ComponentModel.DataAnnotations;

namespace RTROPToLogoIntegration.Application.DTOs
{
    /// <summary>
    /// Kullanıcıdan gelen MRP istek modeli.
    /// Sadece ItemID ve ROP_update_OrderQuantity zorunludur.
    /// Diğer alanlar opsiyoneldir — gönderilmezse MRP_ITEM_PARAMETERS tablosundan okunur.
    /// </summary>
    public class MrpRawItemDto
    {
        [Required(ErrorMessage = "ItemID zorunludur.")]
        public string ItemID { get; set; }

        public double? ROP_update_OrderQuantity { get; set; }

        // --- Opsiyonel Parametreler (null ise DB'den okunur) ---
        public string? ROP_update_ABCDClassification { get; set; }
        public string? PlanningType { get; set; }
        public double? SafetyStock { get; set; }
        public double? ROP { get; set; }
        public double? Max { get; set; }
    }
}
