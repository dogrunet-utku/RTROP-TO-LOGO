namespace RTROPToLogoIntegration.Domain.Models
{
    public class LogoMRPModels
    {
        public string FICHENO { get; set; }      // API'de eksikse ekle
        public string NUMBER { get; set; }       // FICHENO ile aynı olmalı
        public DateTime DATE { get; set; }
        public long TIME { get; set; }           // Legacy: Int64
        public int STATUS { get; set; }          // Default: 1
        public int XML_ATTRIBUTE { get; set; }   // Legacy: 1 (API'de eksik!)
        public int DEMAND_TYPE { get; set; }     // Legacy: 0 (API'de eksik!)
        public int DEMANDTYPE { get; set; }      // Legacy: 0 (API'de eksik!)
        public int USER_NO { get; set; }         // Legacy: Program.LogoUserNumber
        public int USERNO { get; set; }          // Legacy: Program.LogoUserNumber (Tekrar)
        public string MPS_CODE { get; set; }     // Legacy: "MRP"
        public int LINE_CNT { get; set; }
        public Transactions TRANSACTIONS { get; set; }
    }

    public class Transactions
    {
        public List<TransactionItem> items { get; set; }
    }

    public class TransactionItem
    {
        public int ITEMREF { get; set; }
        public int LINE_NO { get; set; }
        public int STATUS { get; set; }          // Default: 1
        public int MRP_HEAD_TYPE { get; set; }   // Legacy: 2
        public int PORDER_TYPE { get; set; }     // Legacy: 0 (API'de eksik!)
        public int BOM_TYPE { get; set; }        // Legacy: 0 (API'de eksik!)
        public int XML_ATTRIBUTE { get; set; }   // Legacy: 1
        public double AMOUNT { get; set; }
        public string UNIT_CODE { get; set; }
        public int SOURCE_INDEX { get; set; }
        public int MEET_TYPE { get; set; }       // 0: Satınalma, 1: Üretim
        public int BOMMASTERREF { get; set; }    // MeetType 1 ise dolu
        public int BOMREVREF { get; set; }       // MeetType 1 ise dolu
        public int CLIENTREF { get; set; }       // MeetType 0 ise dolu
    }
}
