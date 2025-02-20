﻿using Iyzipay;
using Iyzipay.Model;
using Iyzipay.Request;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShoppingApp.Business.Abstract;
using ShoppingApp.Core;
using ShoppingApp.Entity.Concrete;
using ShoppingApp.Entity.Concrete.Identity;
using ShoppingApp.Web.Models.Dtos;
using System.Diagnostics.Metrics;
using System.Reflection.Emit;

namespace ShoppingApp.Web.Controllers
{
    [Authorize]
    public class CardController : Controller
    {
        private readonly UserManager<User> _userManager;
        private readonly ICardService _cardManager;
        private readonly ICardItemService _cardItemManager;
        private readonly IOrderService _orderManager;

        public CardController(UserManager<User> userManager, ICardService cardManager, ICardItemService cardItemManager, IOrderService orderManager)
        {
            _userManager = userManager;
            _cardManager = cardManager;
            _cardItemManager = cardItemManager;
            _orderManager = orderManager;
        }

        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);
            var card = await _cardManager.GetCardByUserId(userId);
            CardDto cardDto = new CardDto
            {
                CardId = card.Id,
                CardItems = card.CardItems.Select(ci => new CardItemDto
                {
                    CardItemId = ci.Id,
                    ProductId = ci.ProductId,
                    ProductName = ci.Product.Name,
                    ProductUrl = ci.Product.Url,
                    ItemPrice = ci.Product.Price,
                    ImageUrl = ci.Product.ImageUrl,
                    Quantity = ci.Quantity
                }).ToList()
            };
            return View(cardDto);
        }

        [HttpPost]
        public IActionResult AddToCard(int productId, int quantity)
        {
            var userId = _userManager.GetUserId(User);
            _cardManager.AddToCard(userId, productId, quantity);
            return RedirectToAction("Index", "Card");
        }

        [HttpPost]

        public async Task<IActionResult> ChangeQuantity(int cardItemId, int quantity)
        {
            await _cardItemManager.ChangeQuantity(cardItemId, quantity);
            return RedirectToAction("Index", "Card");
        }

        public async Task<IActionResult> DeleteFromCard(int id)
        {
            var cardItem = await _cardItemManager.GetByIdAsync(id);
            _cardItemManager.Delete(cardItem);
            return RedirectToAction("Index", "Card");
        }

        public async Task<IActionResult> Checkout()
        {
            var userId = _userManager.GetUserId(User);
            var user = await _userManager.FindByIdAsync(userId);
            var card = await _cardManager.GetCardByUserId(userId);

            OrderDto orderDto = new OrderDto
            {

                FirstName = user.FirstName,
                LastName = user.LastName,
                Address = user.Address,
                City = user.City,
                Phone = user.PhoneNumber,
                Email = user.Email,
                CardDto = new CardDto
                {
                    CardId = card.Id,
                    CardItems = card.CardItems.Select(ci => new CardItemDto
                    {
                        CardItemId = ci.Id,
                        ProductId = ci.ProductId,
                        ProductName = ci.Product.Name,
                        ItemPrice = ci.Product.Price,
                        ImageUrl = ci.Product.ImageUrl,
                        ProductUrl = ci.Product.Url,
                        Quantity = ci.Quantity
                    }).ToList()
                }
            };
            return View(orderDto);
        }


        [HttpPost]
        public async Task<IActionResult> CheckOut(OrderDto orderDto)
        {
            if (ModelState.IsValid)
            {
                var userId = _userManager.GetUserId(User);
                var card = await _cardManager.GetCardByUserId(userId);
                orderDto.CardDto = new CardDto
                {
                    CardId = card.Id,
                    CardItems = card.CardItems.Select(ci => new CardItemDto
                    {
                        CardItemId = ci.Id,
                        ProductId = ci.ProductId,
                        ProductName = ci.Product.Name,
                        ItemPrice = ci.Product.Price,
                        ImageUrl = ci.Product.ImageUrl,
                        ProductUrl = ci.Product.Url,
                        Quantity = ci.Quantity
                    }).ToList()
                };
                //Ödeme işlemleri ile ilgili kodlar buraya yazılacak.
                //1) Kredi kartı numarası kontrolü
                //2) Eger kart numarası gecerli ise odemeyi al
                //3) Ödeme başarılıysa order 'ı al.
                //Yapılan satışı kaydetme işlemleri(Save order)
                if (!CardNumberControl(orderDto.CardNumber))
                {
                    TempData["Message"] = Jobs.CreateMessage("Hata", "Kredi kartı numarası hatalıdır!", "danger");
                    return View(orderDto);
                }


                Payment payment = PaymentProcess(orderDto);
                if (payment.Status == "success")
                {
                    SaveOrder(orderDto, userId);
                    _cardItemManager.ClearCard(orderDto.CardDto.CardId);
                    TempData["Message"] = Jobs.CreateMessage("Başarılı", "Ödemeniz başarıyla alınmıştır.", "success");
                    return View("Success");
                }
                //SaveOrder(orderDto, userId);
                return View();
            }
            return RedirectToAction("Index", "Home");
        }


        [NonAction]
        private async void SaveOrder(OrderDto orderDto, string userId)
        {
            Order order = new Order();
            order.OrderNumber = "SA-" + new Random().Next(111111111, 999999999).ToString();
            order.OrderState = EnumOrderState.Unpaid;//Bu geçici olarak yapıldı aslında buraya ödeme tamamlanmış halde geleceğiz ve unpaid değil comlepte olamsını sağlayacağız.
            order.OrderType = EnumOrderType.CreditCard;
            order.OrderDate = DateTime.Now;
            order.FirstName = orderDto.FirstName;
            order.LastName = orderDto.LastName;
            order.Address = orderDto.Address;
            order.Email = orderDto.Email;
            order.Phone = orderDto.Phone;
            order.City = orderDto.City;
            order.UserId = userId;
            order.OrderItems = new List<Entity.Concrete.OrderItem>();
            foreach (var item in orderDto.CardDto.CardItems)
            {
                var orderItem = new Entity.Concrete.OrderItem();
                orderItem.Price = item.ItemPrice;
                orderItem.Quantity = item.Quantity;
                orderItem.ProductId = item.ProductId;
                order.OrderItems.Add(orderItem);
            }
            await _orderManager.CreateAsync(order);
        }


        [NonAction]
        private bool CardNumberControl(string cardNumber)
        {
            #region Comment
            /*
         *-cardNumber boşluk karakterlerinden temizlensin
         *-cardNumber uzunluk kontrolü yapalım
         *-sayısal değer kontrolü
         *-Luhn algoritmasını uygulamak için gerekeni yap.
         */
            #endregion
            cardNumber = cardNumber.Replace("-", "").Replace(" ", "");// boşluk ve tire(-) den kurtardık.
            if (cardNumber.Length != 16) { return false; }

            foreach (var chr in cardNumber)
            {
                if (!Char.IsNumber(chr)) { return false; } // Cardnumber ın içidekiler rakam değilse false döndür

            }
            return true;

            int ovenTotal = 0;
            int oddTotal = 0;
            for (int i = 0; i < cardNumber.Length; i += 2) // 5282 0802 0003 1680
            {
                int nextOddNumber = Convert.ToInt32(cardNumber[i].ToString());
                int nextOvenNumber = Convert.ToInt32(cardNumber[i + 1].ToString());
                int addedOddNumber = nextOddNumber * 2;
                addedOddNumber = addedOddNumber > 10 ? addedOddNumber - 9 : addedOddNumber;
                oddTotal += nextOddNumber;
                ovenTotal += nextOddNumber;
            }
            int total = oddTotal + ovenTotal;
            bool isValidNumber = total % 10 == 0 ? true : false;
            return isValidNumber;
        }


        [NonAction]
        private Payment PaymentProcess(OrderDto orderDto)
        {
            Options options = new Options();
            options.ApiKey = "sandbox-qxJdO5WfHzIKs6RmhR6tqgaD5lCZcRYy";
            options.SecretKey = "sandbox-i7pAJ3g7OlSPQBVGirjHqUOQDrkvqNiE";
            options.BaseUrl = "https://sandbox-api.iyzipay.com";

            CreatePaymentRequest request = new CreatePaymentRequest
            {
                Locale = Locale.TR.ToString(),
                ConversationId = new Random().Next(1111111, 9999999).ToString(),
                Price = Convert.ToInt32(orderDto.CardDto.TotalPrice()).ToString(),
                PaidPrice = Convert.ToInt32(orderDto.CardDto.TotalPrice()).ToString(),
                Currency = Currency.TRY.ToString(),
                Installment = 1,
                BasketId = orderDto.CardDto.CardId.ToString(),
                PaymentChannel = PaymentChannel.WEB.ToString(),
                PaymentGroup = PaymentGroup.PRODUCT.ToString(),
                PaymentCard = new PaymentCard
                {
                    CardHolderName = orderDto.CardName,
                    CardNumber = orderDto.CardNumber,
                    ExpireMonth = orderDto.ExpirationMonth,
                    ExpireYear = orderDto.ExpirationYear,
                    Cvc = orderDto.Cvc,
                    RegisterCard = 0
                },
                Buyer = new Buyer
                {
                    Id = "BY789",
                    Name = "Wissen",
                    Surname = "Akademi",
                    GsmNumber = "+905559996655",
                    Email = "wissen@mail.com",
                    IdentityNumber = "7500880099457",
                    RegistrationAddress = "Barbaros Bulvarı Yıldız İş Merkezi Kat :3",
                    Ip = "85.34.112.78",
                    City = "İstanbul",
                    Country = "Türkiye"
                },
                ShippingAddress = new Address
                {
                    ContactName = "Sema Doğan",
                    City = "İstanbul",
                    Country = "Türkiye",
                    Description = "Barbaros Bulvarı Yıldız İş Merkezi Kat :3"
                },
                BillingAddress = new Address
                {
                    ContactName = "Aylin Doğmayan",
                    City = "İstanbul",
                    Country = "Türkiye",
                    Description = "Barbaros Bulvarı Yıldız İş Merkezi Kat :3"
                }
            };

            List<BasketItem> basketItems = new List<BasketItem>();
            BasketItem basketItem;
            foreach (var item in orderDto.CardDto.CardItems)
            {
                basketItem = new BasketItem();
                basketItem.Id = item.CardItemId.ToString();
                basketItem.Name = item.ProductName.ToString();
                basketItem.Category1 = "Genel"; //Bu bize aslında cardDto içindeki productların category bilgilerini de doldurmamız gerektiğini hatırlattı.
                basketItem.ItemType = BasketItemType.PHYSICAL.ToString();
                basketItem.Price = Convert.ToInt32(item.ItemPrice * item.Quantity).ToString();
                basketItems.Add(basketItem);
            }
            request.BasketItems = basketItems;
            Payment payment = Payment.Create(request, options);
            return payment;

        }

        public async Task<ActionResult> GetOrders()
        {
            var userId = _userManager.GetUserId(User);
            var orders = await _orderManager.GetOrders(userId);
            List<OrderListDto> orderListDtos = new List<OrderListDto>();
            OrderListDto orderListDto;
            foreach (var order in orders)
            {
                orderListDto = new OrderListDto()
                {
                    OrderId = order.Id,
                    OrderNumber = order.OrderNumber,
                    OrderDate = order.OrderDate,
                    FirstName = order.FirstName,
                    LastName = order.LastName,
                    Address = order.Address,
                    City = order.City,
                    Phone = order.Phone,
                    Email = order.Email,
                    OrderState = order.OrderState,
                    OrderType = order.OrderType,
                    OrderListItems = order.OrderItems.Select(oi => new OrderListItemDto
                    {
                        OrderListItemId = oi.Id,
                        ProductName = oi.Product.Name,
                        Price = oi.Price,
                        Quantity = oi.Quantity,
                        ImageUrl = oi.Product.ImageUrl,
                        ProductUrl = oi.Product.Url
                    }).ToList()
                };
                orderListDtos.Add(orderListDto);
            }
            return View(orderListDtos);
        }

    }
}
