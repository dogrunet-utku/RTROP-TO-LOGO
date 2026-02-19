namespace RTROPToLogoIntegration.Application.DTOs
{
    /// <summary>
    /// MRP işlem sonuç modeli.
    /// </summary>
    public class MrpResultDto
    {
        /// <summary>Toplam gönderilen ürün sayısı.</summary>
        public int SentCount { get; set; }

        /// <summary>Başarıyla işlenen (UPSERT + MRP hesaplama yapılan) ürün sayısı.</summary>
        public int UpdateCount { get; set; }

        /// <summary>Başarısız olan ürün sayısı.</summary>
        public int FailedCount { get; set; }

        /// <summary>Başarısız olan ürün kodları listesi.</summary>
        public List<string> FailedItems { get; set; } = new();

        /// <summary>Talep fişi oluşturuldu mu?</summary>
        public bool DemandSlipCreated { get; set; }

        /// <summary>Oluşturulan talep fişi numarası (varsa).</summary>
        public string? FicheNo { get; set; }

        /// <summary>Talep fişine eklenen satır sayısı.</summary>
        public int DemandLineCount { get; set; }
    }
}
