using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SSW.MusicStore.API.ViewModels;
using Microsoft.AspNetCore.Authorization;

using System;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SSW.MusicStore.API.Helpers;
using SSW.MusicStore.API.Settings;
using SSW.MusicStore.BusinessLogic.Interfaces.Command;
using SSW.MusicStore.BusinessLogic.Interfaces.Query;
using SSW.MusicStore.Data.Entities;

namespace SSW.MusicStore.API.Controllers
{
	[Route("api")]
    public class ShoppingCartController : Controller
    {
		private readonly IAlbumQueryService _albumQueryService;
        private readonly ICartQueryService _cartQueryService;
        private readonly ICartCommandService _cartCommandService;
        private readonly ILogger _logger;
        private readonly AppSettings _appSettings;

		public ShoppingCartController(
			ILoggerFactory loggerfactory,
			IAlbumQueryService albumQueryService,
            ICartQueryService cartQueryService,
            ICartCommandService cartCommandService,
            IOptions<AppSettings> appSettingsOptions)
		{
			_albumQueryService = albumQueryService;
		    _cartQueryService = cartQueryService;
		    _cartCommandService = cartCommandService;
		    _logger = loggerfactory.CreateLogger(nameof(StoreController));
		    _appSettings = appSettingsOptions.Value;
		}

        /// <summary>
        /// Gets the current shopping cart for logged in user.
        /// </summary>
        /// <returns>Cart object including shopping cart items and totals</returns>
        [Authorize(ActiveAuthenticationSchemes = "Bearer")]
        [HttpGet("cart/current")]
        public async Task<IActionResult> GetCurrentCart()
        {
            // Get current cart for the logged in user
            var viewModel = await GetCart();

            // Return the cart json
            return Json(viewModel);
        }

        /// <summary>
        /// Gets the current shopping cart for logged in user.
        /// </summary>
        /// <returns>Cart object including shopping cart items and totals</returns>
        [Authorize(ActiveAuthenticationSchemes = "Bearer")]
        [HttpGet("cart/items")]
        public async Task<IActionResult> GetCartItems()
        {
            // Get current cart for the logged in user
            var viewModel = await GetCart();
            
            // Return the cart json
            return Json(viewModel.CartItems);
        }

        /// <summary>
        /// Adds album to cart.
        /// </summary>
        /// <param name="id">The identifier.</param>
        /// <returns>Cart object including shopping cart items and totals</returns>
        [Authorize(ActiveAuthenticationSchemes = "Bearer")]
        [HttpPost("cart/add/{id}")]
        public async Task<IActionResult> AddToCart(int id)
        {
            // Retrieve the album from the database
            var addedAlbum = await _albumQueryService.GetAlbumDetails(id);

            // Add it to the shopping cart
            await _cartCommandService.AddToCart(GetCartId(), addedAlbum);

            // Return the cart json
            var viewModel = await GetCart();
            return Json(viewModel);
        }


		/// <summary>
		/// Gets the current orders for logged in user.
		/// </summary>
		/// <returns>Order object </returns>
		[Authorize(ActiveAuthenticationSchemes = "Bearer")]
		[HttpGet("order/all")]
		public async Task<IActionResult> GetOrders()
		{
			// Add it to the order
			var viewModel = await _cartQueryService.GetOrders(GetCartId());

			// Return the order json
			return Json(viewModel);
		}

	    /// <summary>Create order from cart.
	    /// </summary>
	    /// <param name="order">Order to create</param>
	    /// <returns>Cart object including shopping cart items and totals</returns>
	    [Authorize(ActiveAuthenticationSchemes = "Bearer")]
		[HttpPost("order/create")]
		public async Task<IActionResult> CreateOrderFromCart([FromBody] OrderViewModel order)
		{
			var addedOrder = new Order
			{
				Address = order.Address,
				City = order.City,
				Country = order.Country,
				Email = order.Email,
				FirstName = order.FirstName,
				LastName = order.LastName,
				OrderDate = DateTime.Today,
				Phone = order.Phone,
				PostalCode = order.PostalCode,
				State = order.State ?? "NA",
				Username = GetCartId(),
				Total = 0
			};

		    // Add it to the order
			var viewModel = await _cartCommandService.CreateOrderFromCart(GetCartId(), addedOrder, order.StripeToken, _appSettings.Stripe.SecretKey);

			// Return the order json
			return Json(viewModel);
		}

		/// <summary>
		/// Empties the cart.
		/// </summary>
		/// <returns>Cart object including shopping cart items and totals</returns>
		[Authorize(ActiveAuthenticationSchemes = "Bearer")]
        [HttpPost("cart/clear")]
        public async Task<IActionResult> EmptyCart()
        {
            // Clear shopping cart
            await this._cartCommandService.EmptyCart(GetCartId());

            // Return the cart json
            var viewModel = await GetCart();
            return Json(viewModel);
        }

        /// <summary>
        /// Removes specified item from cart.
        /// </summary>
        /// <param name="id">The identifier.</param>
        /// <returns>Cart object including shopping cart items and totals</returns>
        [Authorize(ActiveAuthenticationSchemes = "Bearer")]
        [HttpDelete("cart/remove/{id}")]
        public async Task<IActionResult> RemoveFromCart(int id)
        {
            // Remove item from the shopping cart
            await _cartCommandService.RemoveCartItem(id);

            // Return the cart json
            var viewModel = await GetCart();
            return Json(viewModel);
        }

        private async Task<ShoppingCartViewModel> GetCart()
        {
            var userId = GetCartId();
            var cart = await this._cartQueryService.GetCart(userId);

            // Set up our ViewModel
            var viewModel = new ShoppingCartViewModel
            {
                CartItems = cart.CartItems.Select(c => new CartItem
                {
                    Album = c.Album,
                    CartId = c.CartId,
                    Count = c.Count,
                    DateCreated = c.DateCreated,
                    CartItemId = c.CartItemId
                }).ToList(),
                CartTotal = cart.GetTotal()
            };

            return viewModel;
        }

		private string GetCartId()
		{
		    var userId = this.User.Claims.GetNameIdentifier();
            if (userId.IsNullOrEmpty())
            {
                var message = "Could not find name identifier in user claims";
                _logger.LogError(message);
                throw new MusicStoreException(message);
            }

            return userId;
        }
    }
}