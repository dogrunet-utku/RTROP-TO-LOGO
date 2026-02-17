using Dapper;
using Microsoft.Data.SqlClient;

namespace RTROPToLogoIntegration.Infrastructure.Persistence
{
    /// <summary>
    /// Logo veritabanı stok işlemleri (Dapper).
    /// </summary>
    public class StockRepository
    {
        private readonly IConfiguration _configuration;

        public StockRepository(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string GetConnectionString()
        {
            return _configuration.GetConnectionString("LogoConnection");
        }

        /// <summary>
        /// Gelecek Mal (Açık Sipariş) miktarını getirir.
        /// Kaynak: Form1.cs (Legacy Logic)
        /// </summary>
        public async Task<double> GetOpenPoQuantityAsync(int itemRef, string firmNo, string periodNr)
        {
            using (var connection = new SqlConnection(GetConnectionString()))
            {
                // Form1.cs'ten alınan orijinal sorgu
                // Tablo isimleri parametrik yapıldı.
                string sql = $@"
                    SELECT SUM(ORFL.AMOUNT - ORFL.SHIPPEDAMOUNT) AS TOTAL 
                    FROM LG_{firmNo}_{periodNr}_ORFLINE ORFL
                    INNER JOIN LG_{firmNo}_{periodNr}_ORFICHE ORF ON ORF.LOGICALREF = ORFL.ORDFICHEREF
                    INNER JOIN LG_{firmNo}_ITEMS ITM ON ITM.LOGICALREF = ORFL.STOCKREF
                    WHERE ORF.TRCODE=2 AND ORFL.TRCODE=2 AND ITM.ACTIVE=0 
                    AND ORF.CANCELLED=0 AND ORFL.CANCELLED=0 AND ORFL.CLOSED=0 
                    AND (ORFL.AMOUNT - ORFL.SHIPPEDAMOUNT) > 0 
                    AND ORF.STATUS=4 AND ITM.LOGICALREF=@ItemRef";

                // Dapper decimal dönebilir, null check ile double yapıyoruz.
                var result = await connection.ExecuteScalarAsync<object>(sql, new { ItemRef = itemRef });
                return Convert.ToDouble(result ?? 0);
            }
        }

        /// <summary>
        /// Malzeme Kodu'ndan (ItemID) LOGICALREF getirir.
        /// </summary>
        public async Task<int> GetItemRefByCodeAsync(string itemCode, string firmNo)
        {
            using (var connection = new SqlConnection(GetConnectionString()))
            {
                string sql = $@"
                    SELECT LOGICALREF 
                    FROM LG_{firmNo}_ITEMS WITH(NOLOCK) 
                    WHERE CODE = @Code";

                var result = await connection.ExecuteScalarAsync<int?>(sql, new { Code = itemCode });
                return result ?? 0;
            }
        }

        /// <summary>
        /// Eldeki Stok (On Hand) miktarını getirir.
        /// Kaynak: Form1.cs (Legacy Logic - GNTOTST)
        /// </summary>
        public async Task<double> GetStockQuantityAsync(int itemRef, string firmNo, string periodNr)
        {
            using (var connection = new SqlConnection(GetConnectionString()))
            {
                // Form1.cs'ten alınan sorgu.
                // NOT: GNTOTST tablosu/view'ı dönem eki alır: LV_{firm}_{period}_GNTOTST
                string sql = $@"
                    SELECT SUM(ISNULL(ONHAND,0)) as Total 
                    FROM LV_{firmNo}_{periodNr}_GNTOTST 
                    WHERE STOCKREF=@ItemRef AND INVENNO NOT IN (-1,5,6,9,10,11,12)";

                var result = await connection.ExecuteScalarAsync<object>(sql, new { ItemRef = itemRef });
                return Convert.ToDouble(result ?? 0);
            }
        }

        /// <summary>
        /// Stok kartının Özel Kod 2 (SpeCode2) alanını günceller.
        /// Kaynak: Form1.cs
        /// </summary>
        public async Task UpdateItemSpeCode2Async(int itemRef, string speCode2, string firmNo)
        {
            using (var connection = new SqlConnection(GetConnectionString()))
            {
                string sql = $@"
                    UPDATE LG_{firmNo}_ITEMS 
                    SET SPECODE2=@SpeCode2 
                    WHERE LOGICALREF=@ItemRef";

                await connection.ExecuteAsync(sql, new { ItemRef = itemRef, SpeCode2 = speCode2 });
            }
        }

        /// <summary>
        /// Form1.cs -> UpdateINVDef metodunun birebir karşılığıdır.
        /// ABC Kodu, Min, Max ve Güvenlik stoğunu INVDEF tablosunda (INVENNO=0) günceller.
        /// </summary>
        public async Task UpdateInvDefAsync(int itemRef, double minLevel, double maxLevel, double safeLevel, int abcCode, string firmNo, int invenNo)
        {
            // Form1.cs Satır 392 referans alınmıştır. (INVENNO dinamik oldu)
            string sql = $@"
                UPDATE LG_{firmNo}_INVDEF 
                SET SAFELEVEL=@SafeLevel, ABCCODE=@AbcCode, MINLEVEL=@MinLevel, MAXLEVEL=@MaxLevel 
                WHERE INVENNO=@InvenNo AND ITEMREF=@ItemRef";

            using var connection = new SqlConnection(GetConnectionString());
            await connection.ExecuteAsync(sql, new 
            { 
                ItemRef = itemRef, 
                MinLevel = minLevel, 
                MaxLevel = maxLevel, 
                SafeLevel = safeLevel, 
                AbcCode = abcCode,
                InvenNo = invenNo
            });
        }

        /// <summary>
        /// Malzeme Kodu'ndan LOGICALREF, Ana Birim Kodu (UNITCODE) ve Kart Tipi (CARDTYPE) getirir.
        /// Kaynak: Form1.cs (GetItemRef mantığı + UnitSetL join)
        /// </summary>
        public async Task<(int ItemRef, string UnitCode, int CardType)> GetItemRefAndUnitByCodeAsync(string itemCode, string firmNo)
        {
            using (var connection = new SqlConnection(GetConnectionString()))
            {
                // Items tablosu UnitSetRef tutar. UnitSetL (Line) tablosunda MainUnit=1 olan satır ana birimdir.
                string sql = $@"
                    SELECT I.LOGICALREF, U.CODE, I.CARDTYPE
                    FROM LG_{firmNo}_ITEMS I WITH(NOLOCK)
                    LEFT JOIN LG_{firmNo}_UNITSETL U WITH(NOLOCK) ON U.UNITSETREF = I.UNITSETREF AND U.MAINUNIT = 1
                    WHERE I.CODE = @Code";

                var result = await connection.QueryFirstOrDefaultAsync(sql, new { Code = itemCode });
                
                if (result == null) return (0, "", 0);
                return (result.LOGICALREF, result.CODE, result.CARDTYPE);
            }
        }

        /// <summary>
        /// Son MRP Fiş Numarasını getirir veya yenisini üretir.
        /// Format: MRPYYYYMM-XXXXX (Örn: MRP202502-00001)
        /// Kaynak: Form1.cs -> GetLastMRPNumber
        /// </summary>
        public async Task<string> GetLastMRPNumberAsync(string firmNo, string periodNr)
        {
            // Form1.cs'deki mantık: MRP + YYYY + MM + -
            string mrpHeader = "MRP";
            string year = DateTime.Now.ToString("yyyy");
            string month = DateTime.Now.ToString("MM");
            string searchPattern = $"{mrpHeader}{year}{month}-";

            // Form1.cs'deki SQL Sorgusunun Birebir Aynısı (Dapper ile)
            // LG_{firm}_{period}_DEMANDFICHE tablosuna bakılır.
            string sql = $@"
                SELECT TOP 1 
                    CONCAT(
                        LEFT(FICHENO, 11), 
                        RIGHT('00000' + CAST(CAST(RIGHT(FICHENO, 5) AS INT) + 1 AS VARCHAR), 5)
                    ) AS YENI_FICHENO
                FROM LG_{firmNo}_{periodNr}_DEMANDFICHE
                WHERE FICHENO LIKE @SearchPattern + '%' 
                ORDER BY FICHENO DESC";

            using var connection = new SqlConnection(GetConnectionString());
            var newFicheNo = await connection.ExecuteScalarAsync<string>(sql, new { SearchPattern = searchPattern });

            if (string.IsNullOrEmpty(newFicheNo))
            {
                // İlk kayıt: MRP202502-00001
                return $"{searchPattern}000001";
            }

            return newFicheNo;
        }
        /// <summary>
        /// Ürünün tipini (MAMÜL=12, YARI=11, HAM=10) bulur.
        /// </summary>
        public async Task<string> GetCardTypeAsync(string itemCode, string firmNo)
        {
            string sql = $"SELECT CARDTYPE FROM LG_{firmNo}_ITEMS WITH(NOLOCK) WHERE CODE=@Code";
            using var conn = new SqlConnection(GetConnectionString());
            var result = await conn.ExecuteScalarAsync<object>(sql, new { Code = itemCode });
            return result?.ToString() ?? "";
        }

        /// <summary>
        /// Ürünün BOMMASTERREF ve VALIDREVREF bilgilerini bulur (Form1.cs -> GetBomref).
        /// </summary>
        public async Task<(int BomRef, int BomRevRef)> GetBomInfoAsync(int itemRef, string firmNo)
        {
            string sql = $@"SELECT TOP 1 LOGICALREF, VALIDREVREF FROM LG_{firmNo}_BOMASTER WITH(NOLOCK) WHERE MAINPRODREF=@ItemRef ORDER BY LOGICALREF DESC";
            using var conn = new SqlConnection(GetConnectionString());
            var result = await conn.QueryFirstOrDefaultAsync(sql, new { ItemRef = itemRef });
            
            if (result == null) return (0, 0);
            return (result.LOGICALREF, result.VALIDREVREF);
        }

        /// <summary>
        /// Hammadde ise tedarikçisini (CLIENTREF) bulur (Form1.cs -> GetClientRef).
        /// </summary>
        public async Task<int> GetClientRefAsync(int itemRef, string firmNo)
        {
            string sql = $@"SELECT TOP 1 CLIENTREF FROM LG_{firmNo}_SUPPASGN WITH(NOLOCK) WHERE ITEMREF=@ItemRef AND CLCARDTYPE=1 ORDER BY PRIORITY ASC";
            using var conn = new SqlConnection(GetConnectionString());
            var result = await conn.ExecuteScalarAsync<int?>(sql, new { ItemRef = itemRef });
            return result ?? 0;
        }
    }
}
