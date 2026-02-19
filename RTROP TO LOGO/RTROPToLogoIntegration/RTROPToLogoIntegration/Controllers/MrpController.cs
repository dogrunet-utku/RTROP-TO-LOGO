using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RTROPToLogoIntegration.Application.DTOs;
using RTROPToLogoIntegration.Application.Features.MRP.Commands;
using RTROPToLogoIntegration.Infrastructure.Services;
using Serilog;
using Wolverine;

namespace RTROPToLogoIntegration.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class MrpController : ControllerBase
    {
        private readonly IMessageBus _bus;
        private readonly ExcelService _excelService;
        private readonly IConfiguration _configuration;

        public MrpController(IMessageBus bus, ExcelService excelService, IConfiguration configuration)
        {
            _bus = bus;
            _excelService = excelService;
            _configuration = configuration;
        }

        /// <summary>
        /// JSON verisi ile MRP hesaplar ve Logo'ya işler.
        /// </summary>
        [HttpPost("calculate-json")]
        public async Task<IActionResult> CalculateJson([FromBody] List<MrpRawItemDto> items)
        {
            if (items == null || items.Count == 0)
                return BadRequest("Liste boş olamaz.");

            // Firm ve Period bilgisi Header veya Config'den alınabilir.
            if (!Request.Headers.TryGetValue("x-firm-no", out var firmNo))
                return BadRequest("x-firm-no header is required.");

            var periodNr = _configuration["Logo:PeriodNumber"] ?? "01";

            var command = new ProcessMrpCommand
            {
                Items = items,
                FirmNo = firmNo.ToString(),
                PeriodNr = periodNr
            };

            try
            {
                var result = await _bus.InvokeAsync<MrpResultDto>(command);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                Log.Warning(ex, "MRP isteği validasyon hatası.");
                return BadRequest(new
                {
                    Success = false,
                    Message = ex.Message
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "MRP JSON işlemi hatası.");
                return StatusCode(500, new 
                { 
                    Success = false, 
                    Message = "İşlem sırasında hata oluştu.", 
                    ErrorDetails = ex.Message 
                });
            }
        }

        /// <summary>
        /// Excel dosyası ile MRP hesaplar ve Logo'ya işler.
        /// </summary>
        [HttpPost("calculate-excel")]
        public async Task<IActionResult> CalculateExcel(IFormFile file)
        {
             if (file == null || file.Length == 0)
                return BadRequest("Dosya yüklenmedi.");

            if (!Request.Headers.TryGetValue("x-firm-no", out var firmNo))
                return BadRequest("x-firm-no header gerekli.");

            var periodNr = _configuration["Logo:PeriodNumber"] ?? "01";

            try
            {
                var items = _excelService.ParseExcel(file);
                
                var command = new ProcessMrpCommand
                {
                    Items = items,
                    FirmNo = firmNo.ToString(),
                    PeriodNr = periodNr
                };

                var result = await _bus.InvokeAsync<MrpResultDto>(command);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                Log.Warning(ex, "MRP isteği validasyon hatası.");
                return BadRequest(new
                {
                    Success = false,
                    Message = ex.Message
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "MRP Excel işlemi hatası.");
                return StatusCode(500, new 
                { 
                    Success = false, 
                    Message = "İşlem sırasında hata oluştu.", 
                    ErrorDetails = ex.Message 
                });
            }
        }
    }
}
