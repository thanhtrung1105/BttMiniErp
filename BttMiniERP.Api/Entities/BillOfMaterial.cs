namespace BttMiniERP.Api.Entities;

public sealed class BillOfMaterial
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int FinishedProductId { get; set; }
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<BillOfMaterialDetail> Details { get; set; } = [];
}
