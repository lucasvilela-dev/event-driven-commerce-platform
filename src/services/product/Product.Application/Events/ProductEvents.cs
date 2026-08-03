namespace Product.Application.Events;

public static class ProductEvents
{
    public const string AggregateType = "Product";

    public static class Topics
    {
        public const string Created = "product.events.created";
        public const string PriceUpdated = "product.events.price-updated";
        public const string Activated = "product.events.activated";
        public const string Deactivated = "product.events.deactivated";
    }

    public static class EventTypes
    {
        public const string ProductCreated = "ProductCreated";
        public const string ProductPriceUpdated = "ProductPriceUpdated";
        public const string ProductActivated = "ProductActivated";
        public const string ProductDeactivated = "ProductDeactivated";
    }
}