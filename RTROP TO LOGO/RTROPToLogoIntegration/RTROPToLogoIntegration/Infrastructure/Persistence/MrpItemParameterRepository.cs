using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RTROPToLogoIntegration.Domain.Entities;
using RTROPToLogoIntegration.Infrastructure.Identity;

namespace RTROPToLogoIntegration.Infrastructure.Persistence
{
    /// <summary>
    /// MRP_ITEM_PARAMETERS tablosu veri erişim katmanı.
    /// Command (UPSERT): EF Core
    /// Query (Read): Dapper
    /// </summary>
    public class MrpItemParameterRepository
    {
        private readonly AppIdentityDbContext _dbContext;
        private readonly IConfiguration _configuration;

        public MrpItemParameterRepository(AppIdentityDbContext dbContext, IConfiguration configuration)
        {
            _dbContext = dbContext;
            _configuration = configuration;
        }

        private string GetAppConnectionString()
        {
            return _configuration.GetConnectionString("DefaultConnection");
        }

        /// <summary>
        /// Parametre kaydını UPSERT eder (varsa güncelle, yoksa ekle). EF Core kullanır.
        /// </summary>
        public async Task UpsertAsync(string firmNo, string itemId, string? abcd, string? planningType,
            double? safetyStock, double? rop, double? max, double? orderQuantity)
        {
            var existing = await _dbContext.MrpItemParameters
                .FirstOrDefaultAsync(x => x.FirmNo == firmNo && x.ItemID == itemId);

            if (existing != null)
            {
                // UPDATE: Sadece gönderilen (null olmayan) alanları güncelle
                if (abcd != null) existing.ABCDClassification = abcd;
                if (planningType != null) existing.PlanningType = planningType;
                if (safetyStock.HasValue) existing.SafetyStock = safetyStock.Value;
                if (rop.HasValue) existing.ROP = rop.Value;
                if (max.HasValue) existing.Max = max.Value;
                if (orderQuantity.HasValue) existing.OrderQuantity = orderQuantity.Value;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                // INSERT: Yeni kayıt oluştur
                var entity = new MrpItemParameter
                {
                    FirmNo = firmNo,
                    ItemID = itemId,
                    ABCDClassification = abcd,
                    PlanningType = planningType,
                    SafetyStock = safetyStock ?? 0,
                    ROP = rop ?? 0,
                    Max = max ?? 0,
                    OrderQuantity = orderQuantity ?? 0,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _dbContext.MrpItemParameters.Add(entity);
            }

            await _dbContext.SaveChangesAsync();
        }

        /// <summary>
        /// Firma ve ItemID bazında parametre kaydını okur. Dapper kullanır (CQRS Query).
        /// </summary>
        public async Task<MrpItemParameter?> GetByFirmAndItemAsync(string firmNo, string itemId)
        {
            const string sql = @"
                SELECT Id, FirmNo, ItemID, ABCDClassification, PlanningType,
                       SafetyStock, ROP, [Max], OrderQuantity, CreatedAt, UpdatedAt
                FROM MRP_ITEM_PARAMETERS WITH(NOLOCK)
                WHERE FirmNo = @FirmNo AND ItemID = @ItemID";

            using var connection = new SqlConnection(GetAppConnectionString());
            return await connection.QueryFirstOrDefaultAsync<MrpItemParameter>(sql, new { FirmNo = firmNo, ItemID = itemId });
        }
    }
}
