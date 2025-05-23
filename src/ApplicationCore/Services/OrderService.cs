using System;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Ardalis.GuardClauses;
using BlazorShared;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderService : IOrderService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IRepository<Basket> _basketRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IAppLogger<OrderService> _logger;
    private readonly ServiceBusSender _serviceBusSender;
    private readonly IHttpClientFactory _httpClientFactory;

    public OrderService(IRepository<Basket> basketRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository,
        IUriComposer uriComposer,
        IAppLogger<OrderService> logger,
        ServiceBusSender serviceBusSender,
        IHttpClientFactory httpClientFactory)
    {
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
        _basketRepository = basketRepository;
        _itemRepository = itemRepository;
        _logger = logger;
        _serviceBusSender = serviceBusSender;
        _httpClientFactory = httpClientFactory;
    }

    public async Task CreateOrderAsync(int basketId, Address shippingAddress)
    {
        var basketSpec = new BasketWithItemsSpecification(basketId);
        var basket = await _basketRepository.FirstOrDefaultAsync(basketSpec);

        Guard.Against.Null(basket, nameof(basket));
        Guard.Against.EmptyBasketOnCheckout(basket.Items);

        var catalogItemsSpecification = new CatalogItemsSpecification(basket.Items.Select(item => item.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

        var items = basket.Items.Select(basketItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            var orderItem = new OrderItem(itemOrdered, basketItem.UnitPrice, basketItem.Quantity);
            return orderItem;
        }).ToList();

        var order = new Order(basket.BuyerId, shippingAddress, items);

        await _orderRepository.AddAsync(order);
        await ReserveOrderAsync(order);
        await DeliveryOrderAsync(order);
    }

    private async Task ReserveOrderAsync(Order order)
    {
        var payload = new
        {
            Id = order.Id.ToString(),
            CustomerId = order.BuyerId,
            Address = order.ShipToAddress,
            TotalAmount = order.Total(),
            Items = order.OrderItems.Select(x => new
            {
                ItemId = x.Id,
                Quantity = x.Units,
                Amount = x.UnitPrice,
            }).ToArray()
        };

        var json = JsonSerializer.Serialize(payload);
        var message = new ServiceBusMessage(json)
        {
            ContentType = MediaTypeNames.Application.Json,
            MessageId = order.Id.ToString()
        };

        try
        {
            await _serviceBusSender.SendMessageAsync(message);
            _logger.LogInformation("Order {OrderId} sent for reservation", order.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ex.Message);
        }
    }

    private async Task DeliveryOrderAsync(Order order)
    {
        var model = new
        {
            Id = order.Id.ToString(),
            CustomerId = order.BuyerId,
            Address = order.ShipToAddress,
            TotalAmount = order.Total(),
            Items = order.OrderItems.Select(x => new
            {
                ItemId = x.Id,
                Quantity = x.Units,
                Amount = x.UnitPrice,
            }).ToArray()
        };

        var json = JsonSerializer.Serialize(model);
        var content = new StringContent(json, Encoding.UTF8, MediaTypeNames.Application.Json);

        try
        {
            var httpClient = _httpClientFactory.CreateClient(nameof(BaseUrlConfiguration.DeliveryOrderProcessor));
            var uriBuilder = new UriBuilder(httpClient.BaseAddress!)
            {
                Path = "api/DeliveryOrderProcessor"
            };

            var response = await httpClient.PostAsync(uriBuilder.Uri, content);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ex.Message);
        }
    }
}
