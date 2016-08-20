using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autofac.Features.OwnedInstances;
using Microsoft.EntityFrameworkCore;
using SSW.DataOnion.Interfaces;
using SSW.MusicStore.BusinessLogic.Interfaces.Command;
using SSW.MusicStore.Data.Entities;
using Stripe;

namespace SSW.MusicStore.BusinessLogic.Command
{
    public class CartCommandService : ICartCommandService
    {
        private readonly Func<Owned<IUnitOfWork>> unitOfWorkFunc;

        public CartCommandService(Func<Owned<IUnitOfWork>> unitOfWorkFunc)
        {
            this.unitOfWorkFunc = unitOfWorkFunc;
        }

        public async Task EmptyCart(string cartId, CancellationToken cancellationToken = new CancellationToken())
        {
            Serilog.Log.Logger.Debug($"{nameof(this.EmptyCart)} for cart id '{cartId}'");
            using (var unitOfWork = this.unitOfWorkFunc())
            {
                var repository = unitOfWork.Value.Repository<CartItem>();
                var cartItems =
                    await
                        repository
                            .Get(cart => cart.CartId == cartId)
                            .ToArrayAsync(cancellationToken);
                repository.DeleteRange(cartItems);

                await unitOfWork.Value.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task<string> ExecuteTransaction(string stripeToken, string stripeSecretKey, int amount)
        {
            var chargeOptions = new StripeChargeCreateOptions()
            {
                Amount = amount,
                Currency = "AUD",
                SourceTokenOrExistingSourceId = stripeToken
            };

            var client = new StripeChargeService(stripeSecretKey);
            var result = client.Create(chargeOptions);

            if (!result.Paid)
            {
                throw new Exception(result.FailureMessage);
            }

            return result.Id;
        }

        public async Task<int> CreateOrderFromCart(string cartId, Order order, string stripeToken, string stripeSecretKey, CancellationToken cancellationToken = new CancellationToken())
        {
            Serilog.Log.Logger.Debug($"{nameof(this.EmptyCart)} for cart id '{cartId}'");
            using (var unitOfWork = this.unitOfWorkFunc())
            {
                var orderRepository = unitOfWork.Value.Repository<Order>();
                var orderDetailsRepository = unitOfWork.Value.Repository<OrderDetail>();
                var cartItemsRepository = unitOfWork.Value.Repository<CartItem>();
                var albumRepository = unitOfWork.Value.Repository<Album>();

                decimal orderTotal = 0;
                orderRepository.Add(order);

                var cartItems =
                    await
                        cartItemsRepository.Get(cart => cart.CartId == cartId)
                            .Include(c => c.Album)
                            .ToListAsync(cancellationToken);
                // Iterate over the items in the cart, adding the order details for each
                foreach (var item in cartItems)
                {
                    //var album = _db.Albums.Find(item.AlbumId);
                    var album =
                        await albumRepository.Get().SingleAsync(a => a.AlbumId == item.AlbumId, cancellationToken);

                    var orderDetail = new OrderDetail
                    {
                        AlbumId = item.AlbumId,
                        OrderId = order.OrderId,
                        UnitPrice = album.Price,
                        Quantity = item.Count,
                    };

                    // Set the order total of the shopping cart
                    orderTotal += item.Count * album.Price;
                    orderDetailsRepository.Add(orderDetail);
                }

                // Set the order's total to the orderTotal count
                order.Total = orderTotal;

                order.TransactionId = await ExecuteTransaction(stripeToken, stripeSecretKey, Convert.ToInt32(orderTotal*100));
                
                // Empty the shopping cart
                var cartItemsToClear = await cartItemsRepository.Get(cart => cart.CartId == cartId).ToArrayAsync(cancellationToken);
                cartItemsRepository.DeleteRange(cartItemsToClear);

                // Save all the changes
                await unitOfWork.Value.SaveChangesAsync(cancellationToken);

                return order.OrderId;
            }
        }

        public async Task AddToCart(string cartId, Album album, CancellationToken cancellationToken = default(CancellationToken))
        {
            Serilog.Log.Logger.Debug($"{nameof(this.AddToCart)} album '{album.Title}' for cart with id '{cartId}'");
            using (var unitOfWork = this.unitOfWorkFunc())
            {
                var cartRepository = unitOfWork.Value.Repository<Cart>();
                var cartItemsRepository = unitOfWork.Value.Repository<CartItem>();

                // check if cart exists and create one if it doesn't
                var existingCart = await cartRepository.Get().SingleOrDefaultAsync(c => c.CartId == cartId, cancellationToken);
                if (existingCart == null)
                {
                    existingCart = new Cart {CartId = cartId, CartItems = new List<CartItem>()};
                    cartRepository.Add(existingCart);
                }

                // Get the matching cart and album instances
                var cartItem =
                    await
                        cartItemsRepository.Get().SingleOrDefaultAsync(
                            c => c.CartId == cartId && c.AlbumId == album.AlbumId,
                            cancellationToken);

                if (cartItem == null)
                {
                    // Create a new cart item if no cart item exists
                    cartItem = new CartItem
                    {
                        AlbumId = album.AlbumId,
                        CartId = cartId,
                        Count = 1,
                        DateCreated = DateTime.Now
                    };

                    cartItemsRepository.Add(cartItem);
                    existingCart.CartItems.Add(cartItem);
                }
                else
                {
                    // If the item does exist in the cart, then add one to the quantity
                    cartItem.Count++;
                }

                await unitOfWork.Value.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task<int> RemoveCartItem(int cartItemId, CancellationToken cancellationToken = new CancellationToken())
        {
            Serilog.Log.Logger.Debug($"{nameof(this.RemoveCartItem)} for cart item with id '{cartItemId}'");
            using (var unitOfWork = this.unitOfWorkFunc())
            {
                var cartItemsRepository = unitOfWork.Value.Repository<CartItem>();

                // Get the cart
                var cartItem = await cartItemsRepository.Get().SingleOrDefaultAsync(c => c.CartItemId == cartItemId, cancellationToken);

                if (cartItem == null)
                {
                    var message = $"Cart item with id {cartItemId} could not be found.";
                    Serilog.Log.Logger.Error(message);
                    throw new Exception(message);
                }

                var itemCount = 0;

                if (cartItem.Count > 1)
                {
                    cartItem.Count--;
                    itemCount = cartItem.Count;
                }
                else
                {
                    cartItemsRepository.Delete(cartItem);
                }

                await unitOfWork.Value.SaveChangesAsync(cancellationToken);
                return itemCount;
            }
        }
    }

}
