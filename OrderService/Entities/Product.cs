namespace OrderService.Entities;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    
    public string Price { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
}
