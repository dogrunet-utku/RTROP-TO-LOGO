namespace RTROPToLogoIntegration.Domain.Models
{
    public class LogoMRPModels
    {
        public string FICHENO { get; set; }
        public string NUMBER { get; set; } // Phase 14: NUMBER alanı
        public DateTime DATE { get; set; }
        public Transactions TRANSACTIONS { get; set; }
    }

    public class Transactions
    {
        // Küçük harf kalmalı (Logo JSON zorunluluğu)
        public List<TransactionItem> items { get; set; }
    }

    public class TransactionItem
    {
        public int ITEMREF { get; set; }
        public double AMOUNT { get; set; }
        public string UNIT_CODE { get; set; } // Phase 12: Unit Code
        public int MRP_HEAD_TYPE { get; set; } = 2; // Default 2
        
        // Phase 14: Yeni Alanlar
        public int STATUS { get; set; } = 1; // Default 1
        public int MEET_TYPE { get; set; } // 0: Satınalma (Hammadde), 1: Üretim (Mamül/YM)
        public int BOMMASTERREF { get; set; }
        public int BOMREVREF { get; set; }
        public int CLIENTREF { get; set; }
        public int SOURCE_INDEX { get; set; } // Phase 17: Ambar (Warehouse)
    }
}
