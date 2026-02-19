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

        public async Task<MrpResultDto> Handle(ProcessMrpCommand request)
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
            var result = new MrpResultDto { SentCount = request.Items.Count };

            foreach (var item in request.Items)
            {
                // ========== STEP 1: Logo'da ürünü doğrula (UPSERT'ten ÖNCE!) ==========
                var (itemRef, unitCode, _) = await _stockRepository.GetItemRefAndUnitByCodeAsync(item.ItemID, request.FirmNo);

                if (itemRef == 0)
                {
                    Log.Warning("Malzeme Logo'da bulunamadı, atlaniyor ve kaydedilmiyor: {ItemID}", item.ItemID);
                    skippedItems.Add(item.ItemID);
                    continue;
                }

                // ========== STEP 2: UPSERT — Logo'da doğrulandıktan SONRA parametreleri kaydet ==========
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

                // ========== STEP 3: Efektif parametreleri belirle ==========
                string? effectiveAbcd = item.ROP_update_ABCDClassification;
                string? effectivePlanningType = item.PlanningType;
                double effectiveSafetyStock = item.SafetyStock ?? 0;
                double effectiveRop = item.ROP ?? 0;
                double effectiveMax = item.Max ?? 0;
                double? effectiveOrderQty = item.ROP_update_OrderQuantity;

                bool needsDbFallback = string.IsNullOrWhiteSpace(effectivePlanningType) 
                                    || effectiveRop == 0
                                    || !effectiveOrderQty.HasValue;

                if (needsDbFallback)
                {
                    var dbParam = await _paramRepo.GetByFirmAndItemAsync(request.FirmNo, item.ItemID);

                    if (dbParam == null || string.IsNullOrWhiteSpace(dbParam.PlanningType))
                    {
                        Log.Warning("Parametreler eksik ve DB'de kayıt yok. Atlanan malzeme: {ItemID}", item.ItemID);
                        skippedItems.Add(item.ItemID);
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(effectiveAbcd)) effectiveAbcd = dbParam.ABCDClassification;
                    if (string.IsNullOrWhiteSpace(effectivePlanningType)) effectivePlanningType = dbParam.PlanningType;
                    if (effectiveSafetyStock == 0) effectiveSafetyStock = dbParam.SafetyStock;
                    if (effectiveRop == 0) effectiveRop = dbParam.ROP;
                    if (effectiveMax == 0) effectiveMax = dbParam.Max;
                    if (!effectiveOrderQty.HasValue) effectiveOrderQty = dbParam.OrderQuantity;
                }

                // Final validation — OrderQuantity olmadan devam edilemez
                if (!effectiveOrderQty.HasValue || effectiveOrderQty.Value == 0)
                {
                    Log.Warning("OrderQuantity belirlenemedi. Atlanan malzeme: {ItemID}", item.ItemID);
                    skippedItems.Add(item.ItemID);
                    continue;
                }

                // ========== STEP 4: Stok Hesaplama ==========
                var stockQty = await _stockRepository.GetStockQuantityAsync(itemRef, request.FirmNo, request.PeriodNr);
                var openPo = await _stockRepository.GetOpenPoQuantityAsync(itemRef, request.FirmNo, request.PeriodNr);

                var netStock = stockQty + openPo;
                var ropGap = effectiveRop - netStock;
                double need = effectiveOrderQty.Value - ropGap;

                // ========== STEP 5: MTS Kararı ==========
                if (netStock < effectiveRop && effectivePlanningType == "MTS")
                {
                    string cardType = await _stockRepository.GetCardTypeAsync(item.ItemID, request.FirmNo);

                    int sourceIndex = 0;
                    int meetType = 0;
                    int bomRefValue = 0, bomRevRefValue = 0, clientRef = 0;

                    if (cardType == "10")
                    {
                        sourceIndex = hmWare;
                        meetType = 0;
                        clientRef = await _stockRepository.GetClientRefAsync(itemRef, request.FirmNo);
                    }
                    else if (cardType == "11")
                    {
                        sourceIndex = ymWare;
                        meetType = 1;
                        (bomRefValue, bomRevRefValue) = await _stockRepository.GetBomInfoAsync(itemRef, request.FirmNo);
                    }
                    else if (cardType == "12")
                    {
                        sourceIndex = mmWare;
                        meetType = 1;
                        (bomRefValue, bomRevRefValue) = await _stockRepository.GetBomInfoAsync(itemRef, request.FirmNo);
                    }
                    else
                    {
                        Log.Debug("Bilinmeyen CardType: {CardType} Item: {ItemID}", cardType, item.ItemID);
                    }

                    await _stockRepository.UpdateItemSpeCode2Async(itemRef, "MTS", request.FirmNo);

                    string abcRaw = effectiveAbcd?.ToUpper() ?? "";
                    int abcCode = abcRaw switch
                    {
                        "A" => 1,
                        "B" => 2,
                        "C" => 3,
                        _ => 0
                    };

                    await _stockRepository.UpdateInvDefAsync(
                        itemRef, effectiveRop, effectiveMax, effectiveSafetyStock,
                        abcCode, request.FirmNo, sourceIndex
                    );

                    updatedCount++;

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
                        BOMMASTERREF = bomRefValue,
                        BOMREVREF = bomRevRefValue,
                        CLIENTREF = clientRef
                    };

                    mrpList.TRANSACTIONS.items.Add(transItem);
                }
                else
                {
                    // MTS değil veya stok yeterli — UPSERT yapıldı ama talep fişine eklenmedi
                    updatedCount++;
                }
            }

            mrpList.LINE_CNT = mrpList.TRANSACTIONS.items.Count;

            // Result'ı doldur
            result.UpdateCount = updatedCount;
            result.FailedCount = skippedItems.Count;
            result.FailedItems = skippedItems;

            // ========== STEP 6: Logo'ya Gönder ==========
            if (mrpList.TRANSACTIONS.items.Count > 0)
            {
                try
                {
                    await _logoClientService.PostDemandFicheAsync(mrpList, request.FirmNo);
                    result.DemandSlipCreated = true;
                    result.FicheNo = mrpList.FICHENO;
                    result.DemandLineCount = mrpList.TRANSACTIONS.items.Count;
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
                result.DemandSlipCreated = false;
                Log.Information("MRP İşlemi: İşlenecek veya ihtiyaç duyulan MTS kaydı bulunamadı.");
            }

            return result;
        }
    }
}
