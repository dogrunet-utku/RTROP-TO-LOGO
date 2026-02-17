using Microsoft.Extensions.Configuration;
using RTROPToLogoIntegration.Application.DTOs;
using RTROPToLogoIntegration.Domain.Models;
using RTROPToLogoIntegration.Infrastructure.Persistence;
using RTROPToLogoIntegration.Infrastructure.Services;
using Serilog;

namespace RTROPToLogoIntegration.Application.Features.MRP.Commands
{
    public class ProcessMrpCommand
    {
        public List<MrpRawItemDto> Items { get; set; }
        public string FirmNo { get; set; }
        public string PeriodNr { get; set; }
    }

    public class ProcessMrpCommandHandler
    {
        private readonly StockRepository _stockRepository;
        private readonly LogoClientService _logoClientService;
        private readonly IConfiguration _config;

        public ProcessMrpCommandHandler(StockRepository stockRepository, LogoClientService logoClientService, IConfiguration config)
        {
            _stockRepository = stockRepository;
            _logoClientService = logoClientService;
            _config = config;
        }

        public async Task<bool> Handle(ProcessMrpCommand request)
        {
            // Config'den ambarları al
            int mmWare = int.Parse(_config["WarehouseSettings:MM_Ambar"] ?? "3");
            int ymWare = int.Parse(_config["WarehouseSettings:YM_Ambar"] ?? "2");
            int hmWare = int.Parse(_config["WarehouseSettings:HM_Ambar"] ?? "1");
            int logoUser = int.Parse(_config["LogoRestSettings:LogoUserNumber"] ?? "1");

            // 1. Fiş Numarasını Üret (Form1.cs mantığı)
            var ficheNo = await _stockRepository.GetLastMRPNumberAsync(request.FirmNo, request.PeriodNr);

            var mrpList = new LogoMRPModels
            {
                FICHENO = ficheNo,
                NUMBER = ficheNo, // Form1.cs bunu da eşitliyor
                DATE = DateTime.Now,
                TIME = Convert.ToInt64(DateTime.Now.ToString("HHmmss")),
                STATUS = 1,
                XML_ATTRIBUTE = 1,
                DEMAND_TYPE = 0,
                DEMANDTYPE = 0,
                USER_NO = logoUser,
                USERNO = logoUser,
                MPS_CODE = "MRP",
                TRANSACTIONS = new Transactions { items = new List<TransactionItem>() }
            };

            int updatedCount = 0;
            int lineCounter = 1;

            foreach (var item in request.Items)
            {
                // 1. Ref, UnitCode ve CardType Çek (Db'den her zaman taze veri)
                var (itemRef, unitCode, _) = await _stockRepository.GetItemRefAndUnitByCodeAsync(item.ItemID, request.FirmNo);
                
                if (itemRef == 0)
                {
                    Log.Warning($"Malzeme bulunamadı: {item.ItemID}");
                    continue;
                }

                // 2. Stok ve OpenPO Çek (Form1.cs sorguları ile)
                var stockQty = await _stockRepository.GetStockQuantityAsync(itemRef, request.FirmNo, request.PeriodNr);
                var openPo = await _stockRepository.GetOpenPoQuantityAsync(itemRef, request.FirmNo, request.PeriodNr);

                // 3. Hesaplama (Form1.cs mantığı)
                // Form1.cs Formülü: Need = IncomingAmount - (ROP - Stock - OpenPO)
                // Burada item.ROP_update_OrderQuantity -> Incoming Amount
                
                var netStock = stockQty + openPo; 
                var ropGap = item.ROP - netStock; 
            
                double need = item.ROP_update_OrderQuantity - ropGap;
                
                // 4. Karar ve Güncelleme
                if (netStock < item.ROP  && item.PlanningType == "MTS")
                {
                    // 1. TİP VE AMBAR BELİRLEME
                    // Previous call already returned CardType but let's use the dedicated method to be explicit as per instructions
                    string cardType = await _stockRepository.GetCardTypeAsync(item.ItemID, request.FirmNo);
                    
                    int sourceIndex = 0; // Varsayılan
                    int meetType = 0;
                    int bomRef = 0, bomRevRef = 0, clientRef = 0;

                    // Form1.cs Satır 132-146 Mantığı
                    if (cardType == "10") // HAMMADDE
                    {
                        sourceIndex = hmWare;
                        meetType = 0; // Satınalma
                        clientRef = await _stockRepository.GetClientRefAsync(itemRef, request.FirmNo);
                    }
                    else if (cardType == "11") // YARI MAMUL
                    {
                        sourceIndex = ymWare;
                        meetType = 1; // Üretim
                        (bomRef, bomRevRef) = await _stockRepository.GetBomInfoAsync(itemRef, request.FirmNo);
                    }
                    else if (cardType == "12") // MAMUL
                    {
                        sourceIndex = mmWare;
                        meetType = 1; // Üretim
                        (bomRef, bomRevRef) = await _stockRepository.GetBomInfoAsync(itemRef, request.FirmNo);
                    }
                    else 
                    {
                        // Diğer tipler (örn. Ticari Mal - 1) için varsayılanlar
                        // Form1.cs logic'inde bunlar genelde işlenmiyor ama burada fallback olarak kaynak ambarı 0 geçiyoruz.
                        // Güvenlik için loglayabiliriz.
                        Log.Debug($"Bilinmeyen CardType: {cardType} Item: {item.ItemID}");
                    }

                    // Logo'da SPECODE2 (MTS olarak) güncelle
                    await _stockRepository.UpdateItemSpeCode2Async(itemRef, "MTS", request.FirmNo);
                    
                    // 1. ABC Kodunu Hesapla (Form1.cs mantığı)
                    string abcRaw = item.ROP_update_ABCDClassification?.ToUpper() ?? "";
                    int _abcCode = 0;
                    if (abcRaw == "A") _abcCode = 1;
                    else if (abcRaw == "B") _abcCode = 2;
                    else if (abcRaw == "C") _abcCode = 3;
                    else if (abcRaw == "-") _abcCode = 0;

                    // 2. Tek seferde INVDEF güncelle (ABC + Seviyeler)
                    await _stockRepository.UpdateInvDefAsync(
                        itemRef, 
                        item.ROP,       // MinLevel
                        item.Max,       // MaxLevel
                        item.SafetyStock, // SafeLevel
                        _abcCode,       // AbcCode
                        request.FirmNo,
                        sourceIndex     // Dinamik Ambar No
                    );
                    
                    updatedCount++;

                    // 5. Transaction Item Oluştur
                    var transItem = new TransactionItem
                    {
                        ITEMREF = itemRef,
                        LINE_NO = lineCounter++,
                        STATUS = 1,
                        MRP_HEAD_TYPE = 2, // Varsayılan değer
                        PORDER_TYPE = 0,
                        BOM_TYPE = 0,
                        XML_ATTRIBUTE = 1,
                        
                        AMOUNT = need,
                        UNIT_CODE = unitCode, 
                        
                        // YENİ ALANLAR (Form1.cs mantığı)
                        SOURCE_INDEX = sourceIndex, 
                        MEET_TYPE = meetType,
                        BOMMASTERREF = bomRef,
                        BOMREVREF = bomRevRef,
                        CLIENTREF = clientRef
                    };

                    mrpList.TRANSACTIONS.items.Add(transItem);
                }
            }
            
            mrpList.LINE_CNT = mrpList.TRANSACTIONS.items.Count;

            // Logo Gönderimi
            if (mrpList.TRANSACTIONS.items.Count > 0)
            {
                try 
                {
                    await _logoClientService.PostDemandFicheAsync(mrpList, request.FirmNo);
                    Log.Information("MRP İşlemi Tamamlandı. {Count} adet ürün işlendi. Fiş No: {FicheNo}", mrpList.TRANSACTIONS.items.Count, mrpList.FICHENO);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Logo gönderimi başarısız. Fiş No: {FicheNo}", mrpList.FICHENO);
                    throw; // Controller yakalayacak
                }
            }
            else
            {
                Log.Information("MRP İşlemi: İşlenecek veya ihtiyaç duyulan MTS kaydı bulunamadı.");
            }

            return true;
        }
    }
}
