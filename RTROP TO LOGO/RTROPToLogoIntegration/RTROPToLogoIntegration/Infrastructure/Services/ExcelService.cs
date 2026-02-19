using OfficeOpenXml;
using RTROPToLogoIntegration.Application.DTOs;

namespace RTROPToLogoIntegration.Infrastructure.Services
{
    /// <summary>
    /// Excel dosyalarını işleyip verilere dönüştüren servis.
    /// </summary>
    public class ExcelService
    {
        public List<MrpRawItemDto> ParseExcel(IFormFile file)
        {
            var list = new List<MrpRawItemDto>();

            // EPPlus Lisans Ayarı (NonCommercial - Kullanıcı onayı varsayıyoruz)
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using (var stream = new MemoryStream())
            {
                file.CopyTo(stream);
                using (var package = new ExcelPackage(stream))
                {
                    var worksheet = package.Workbook.Worksheets[0];
                    var rowCount = worksheet.Dimension.Rows;

                    // 1. Satır Başlık, 2'den başlıyoruz
                    for (int row = 2; row <= rowCount; row++)
                    {
                        var dto = new MrpRawItemDto();
                        
                        // Sütun Mapping (Excel - DTO):
                        // 1: ItemID (String) -> dto.ItemID
                        // 2: ROP_update_ABCDClassification (String) -> dto.ROP_update_ABCDClassification
                        // 3: Planning Type (String) -> dto.PlanningType
                        // 4: SafetyStock (Double) -> dto.SafetyStock
                        // 5: ROP (Double) -> dto.ROP
                        // 6: Max (Double) -> dto.Max
                        // 7: ROP_update_OrderQuantity (Double) -> dto.ROP_update_OrderQuantity

                        dto.ItemID = worksheet.Cells[row, 1].Value?.ToString();
                        dto.ROP_update_ABCDClassification = worksheet.Cells[row, 2].Value?.ToString();
                        dto.PlanningType = worksheet.Cells[row, 3].Value?.ToString();
                        
                        dto.SafetyStock = worksheet.Cells[row, 4].Value != null ? Convert.ToDouble(worksheet.Cells[row, 4].Value) : null;
                        dto.ROP = worksheet.Cells[row, 5].Value != null ? Convert.ToDouble(worksheet.Cells[row, 5].Value) : null;
                        dto.Max = worksheet.Cells[row, 6].Value != null ? Convert.ToDouble(worksheet.Cells[row, 6].Value) : null;
                        dto.ROP_update_OrderQuantity = worksheet.Cells[row, 7].Value != null ? Convert.ToDouble(worksheet.Cells[row, 7].Value) : null;
                        
                        // ItemType (12) ve Ambar (13) kullanıcıdan istenmiyor.
                        // Mantık arka planda (CardType vb.) halledilecek.

                        // ItemID boşsa satırı atla
                        if (!string.IsNullOrWhiteSpace(dto.ItemID))
                        {
                            list.Add(dto);
                        }
                    }
                }
            }
            return list;
        }
    }
}
