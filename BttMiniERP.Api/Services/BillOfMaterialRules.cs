using BttMiniERP.Api.Common;
using BttMiniERP.Api.DTOs.BillOfMaterials;
using BttMiniERP.Api.Entities;
using BttMiniERP.Api.Enums;

namespace BttMiniERP.Api.Services;

public static class BillOfMaterialRules
{
    public static BillOfMaterialDetail[] Details(CreateBillOfMaterialRequest request)
    {
        MasterDataRules.Validate(request);
        _ = MasterDataRules.NormalizeCode(request.Code);
        var seen = new HashSet<int>();
        foreach (var detail in request.Details)
        {
            if (detail == null) throw new ApiException(400, "Dòng BOM không được null.");
            MasterDataRules.Validate(detail);
            if (detail.RawMaterialId == request.FinishedProductId || !seen.Add(detail.RawMaterialId)
                || detail.QuantityPerUnit <= 0 || detail.QuantityPerUnit > InventoryMovement.MaximumQuantity
                || ((decimal.GetBits(detail.QuantityPerUnit)[3] >> 16) & 255) > 3)
                throw new ApiException(400, "BOM tự tham chiếu, trùng nguyên vật liệu hoặc định mức không hợp lệ.");
        }
        return request.Details.OrderBy(x => x.RawMaterialId).Select(x => new BillOfMaterialDetail
            { RawMaterialId = x.RawMaterialId, QuantityPerUnit = x.QuantityPerUnit }).ToArray();
    }

    public static void RequireItem(Item? item, ItemType expected)
    {
        if (item == null) throw new ApiException(404, "Không tìm thấy Item của BOM.");
        if (!item.IsActive || item.ItemType != expected)
            throw new ApiException(409, "Item BOM phải hoạt động và đúng loại thành phẩm/nguyên vật liệu.");
    }

    public static int NextVersion(int version)
        => version is < 1 or int.MaxValue ? throw new ApiException(409, "Version BOM không thể tăng tiếp.") : version + 1;

    public static void RequireActiveForDeactivation(bool active)
    {
        if (!active) throw new ApiException(409, "BOM đã vô hiệu hóa.");
    }
}
