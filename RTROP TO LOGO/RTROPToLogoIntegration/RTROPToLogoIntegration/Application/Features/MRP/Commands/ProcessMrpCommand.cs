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
        private readonly MrpItemParameterRepository _paramRepo;
        private readonly LogoClientService _logoClientService;
        private readonly IConfiguration _config;

        public ProcessMrpCommandHandler(
            StockRepository stockRepository,
            MrpItemParameterRepository paramRepo,
            LogoClientService logoClientService,
            IConfiguration config)
        {
            _stockRepository = stockRepository;
            _paramRepo = paramRepo;
            _logoClientService = logoClientService;
            _config = config;
        }

        public async Task<bool> Handle(ProcessMrpCommand request)
        {
            // ========== STEP 0: Firma Validasyonu (L_CAPIFIRM) ==========
            var firmExists = await _stockRepository.FirmExistsAsync(request.FirmNo);
            if (!firmExists)
            {
                throw new ArgumentException($"Firma numarası Logo'da bulunamadı: {request.FirmNo}");
            }

            // Config'den ambar ve kullanıcı bilgileri
            int mmWare = int.Parse(_config["WarehouseSettings:MM_Ambar"] ?? "3");
            int ymWare = int.Parse(_config["WarehouseSettings:YM_Ambar"] ?? "2");
            int hmWare = int.Parse(_config["WarehouseSettings:HM_Ambar"] ?? "1");
            int logoUser = int.Parse(_config["LogoRestSettings:LogoUserNumber"] ?? "1");

            // Fiş Numarası Üret
            var ficheNo = await _stockRepository.GetLastMRPNumberAsync(request.FirmNo, request.PeriodNr);

            var mrpList = new LogoMRPModels
            {
                FICHENO = ficheNo,
                NUMBER = ficheNo,
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
            var skippedItems = new List<string>(); // Parametresi eksik atlanmış itemler

            foreach (var item in request.Items)
            {
                // ========== STEP 1: UPSERT — Parametreleri DB'ye kaydet/güncelle ==========
                await _paramRepo.UpsertAsync(
                    request.FirmNo,
                    item.ItemID,
                    item.ROP_update_ABCDClassification,
                    item.PlanningType,
                    item.SafetyStock,
                    item.ROP,
                    item.Max,
                    item.ROP_update_OrderQuantity
                );

                // ========== STEP 2: Efektif parametreleri belirle ==========
                // Gelen değer varsa onu kullan, yoksa DB'den oku
                string? effectiveAbcd = item.ROP_update_ABCDClassification;
                string? effectivePlanningType = item.PlanningType;
                double effectiveSafetyStock = item.SafetyStock ?? 0;
                double effectiveRop = item.ROP ?? 0;
                double effectiveMax = item.Max ?? 0;

                // Herhangi bir zorunlu parametre eksikse DB'den oku
                bool needsDbFallback = string.IsNullOrWhiteSpace(effectivePlanningType) 
                                    || effectiveRop == 0;

                if (needsDbFallback)
                {
                    var dbParam = await _paramRepo.GetByFirmAndItemAsync(request.FirmNo, item.ItemID);

                    if (dbParam == null || string.IsNullOrWhiteSpace(dbParam.PlanningType))
                    {
                        // DB'de de yok — bu item atlanacak
                        Log.Warning("Parametreler eksik ve DB'de kayıt yok. Atlanan malzeme: {ItemID}", item.ItemID);
                        skippedItems.Add(item.ItemID);
                        continue;
                    }

                    // DB'den fallback
                    if (string.IsNullOrWhiteSpace(effectiveAbcd)) effectiveAbcd = dbParam.ABCDClassification;
                    if (string.IsNullOrWhiteSpace(effectivePlanningType)) effectivePlanningType = dbParam.PlanningType;
                    if (effectiveSafetyStock == 0) effectiveSafetyStock = dbParam.SafetyStock;
                    if (effectiveRop == 0) effectiveRop = dbParam.ROP;
                    if (effectiveMax == 0) effectiveMax = dbParam.Max;
                }

                // ========== STEP 3: Logo DB'den ItemRef, UnitCode, CardType çözümle ==========
                var (itemRef, unitCode, _) = await _stockRepository.GetItemRefAndUnitByCodeAsync(item.ItemID, request.FirmNo);

                if (itemRef == 0)
                {
                    Log.Warning("Malzeme Logo'da bulunamadı: {ItemID}", item.ItemID);
                    skippedItems.Add(item.ItemID);
                    continue;
                }

                // ========== STEP 4: Stok Hesaplama ==========
                var stockQty = await _stockRepository.GetStockQuantityAsync(itemRef, request.FirmNo, request.PeriodNr);
                var openPo = await _stockRepository.GetOpenPoQuantityAsync(itemRef, request.FirmNo, request.PeriodNr);

                var netStock = stockQty + openPo;
                var ropGap = effectiveRop - netStock;
                double need = item.ROP_update_OrderQuantity - ropGap;

                // ========== STEP 5: MTS Kararı ==========
                if (netStock < effectiveRop && effectivePlanningType == "MTS")
                {
                    string cardType = await _stockRepository.GetCardTypeAsync(item.ItemID, request.FirmNo);

                    int sourceIndex = 0;
                    int meetType = 0;
                    int bomRef = 0, bomRevRef = 0, clientRef = 0;

                    if (cardType == "10") // HAMMADDE
                    {
                        sourceIndex = hmWare;
                        meetType = 0;
                        clientRef = await _stockRepository.GetClientRefAsync(itemRef, request.FirmNo);
                    }
                    else if (cardType == "11") // YARI MAMUL
                    {
                        sourceIndex = ymWare;
                        meetType = 1;
                        (bomRef, bomRevRef) = await _stockRepository.GetBomInfoAsync(itemRef, request.FirmNo);
                    }
                    else if (cardType == "12") // MAMUL
                    {
                        sourceIndex = mmWare;
                        meetType = 1;
                        (bomRef, bomRevRef) = await _stockRepository.GetBomInfoAsync(itemRef, request.FirmNo);
                    }
                    else
                    {
                        Log.Debug("Bilinmeyen CardType: {CardType} Item: {ItemID}", cardType, item.ItemID);
                    }

                    // SPECODE2 güncelle
                    await _stockRepository.UpdateItemSpeCode2Async(itemRef, "MTS", request.FirmNo);

                    // ABC Kodu
                    string abcRaw = effectiveAbcd?.ToUpper() ?? "";
                    int abcCode = abcRaw switch
                    {
                        "A" => 1,
                        "B" => 2,
                        "C" => 3,
                        _ => 0
                    };

                    // INVDEF güncelle
                    await _stockRepository.UpdateInvDefAsync(
                        itemRef, effectiveRop, effectiveMax, effectiveSafetyStock,
                        abcCode, request.FirmNo, sourceIndex
                    );

                    updatedCount++;

                    // Transaction satırı
                    var transItem = new TransactionItem
                    {
                        ITEMREF = itemRef,
                        LINE_NO = lineCounter++,
                        STATUS = 1,
                        MRP_HEAD_TYPE = 2,
                        PORDER_TYPE = 0,
                        BOM_TYPE = 0,
                        XML_ATTRIBUTE = 1,
                        AMOUNT = need,
                        UNIT_CODE = unitCode,
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

            // ========== STEP 6: Logo'ya Gönder ==========
            if (mrpList.TRANSACTIONS.items.Count > 0)
            {
                try
                {
                    await _logoClientService.PostDemandFicheAsync(mrpList, request.FirmNo);
                    Log.Information("MRP İşlemi Tamamlandı. {Count} adet ürün işlendi. Fiş No: {FicheNo}",
                        mrpList.TRANSACTIONS.items.Count, mrpList.FICHENO);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Logo gönderimi başarısız. Fiş No: {FicheNo}", mrpList.FICHENO);
                    throw;
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
