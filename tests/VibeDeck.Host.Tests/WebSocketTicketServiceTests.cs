using System;
using System.Collections.Generic;
using VibeDeck.Host.Security;
using Xunit;

namespace VibeDeck.Host.Tests
{
    public sealed class WebSocketTicketServiceTests
    {
        private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-26T10:00:00+00:00");

        [Fact]
        public void A_ticket_redeems_once_and_never_again()
        {
            // The whole point: the value that ends up in the WebSocket URL — and therefore
            // in proxy access logs — is spent the moment the real client connects.
            var service = new WebSocketTicketService();
            var ticket = service.Issue("device-a", Now);

            Assert.True(service.TryRedeem(ticket, Now, out var deviceId));
            Assert.Equal("device-a", deviceId);

            Assert.False(service.TryRedeem(ticket, Now, out _), "a replayed ticket must not work");
        }

        [Fact]
        public void A_ticket_expires()
        {
            var service = new WebSocketTicketService();
            var ticket = service.Issue("device-a", Now);

            var tooLate = Now + WebSocketTicketService.Lifetime + TimeSpan.FromSeconds(1);
            Assert.False(service.TryRedeem(ticket, tooLate, out _));
        }

        [Fact]
        public void A_ticket_is_still_valid_just_inside_its_lifetime()
        {
            var service = new WebSocketTicketService();
            var ticket = service.Issue("device-a", Now);

            var justInTime = Now + WebSocketTicketService.Lifetime - TimeSpan.FromSeconds(1);
            Assert.True(service.TryRedeem(ticket, justInTime, out _));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("not-a-real-ticket")]
        public void Unknown_values_are_refused(string value)
        {
            var service = new WebSocketTicketService();
            service.Issue("device-a", Now);

            Assert.False(service.TryRedeem(value, Now, out var deviceId));
            Assert.Null(deviceId);
        }

        [Fact]
        public void Redeeming_one_ticket_does_not_consume_another()
        {
            var service = new WebSocketTicketService();
            var display = service.Issue("device-a", Now);
            var input = service.Issue("device-a", Now);

            Assert.NotEqual(display, input);
            Assert.True(service.TryRedeem(display, Now, out _));
            Assert.True(service.TryRedeem(input, Now, out _));
        }

        [Fact]
        public void The_local_console_has_no_device_identity_and_still_redeems()
        {
            var service = new WebSocketTicketService();
            var ticket = service.Issue(null, Now);

            Assert.True(service.TryRedeem(ticket, Now, out var deviceId));
            Assert.Null(deviceId);
        }

        [Fact]
        public void Tickets_are_unpredictable()
        {
            var service = new WebSocketTicketService();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < 500; i++)
            {
                var ticket = service.Issue("device-a", Now);
                Assert.True(seen.Add(ticket), "tickets must never repeat");
                Assert.True(ticket.Length >= 40, "ticket must carry real entropy");
                Assert.DoesNotContain('+', ticket);
                Assert.DoesNotContain('/', ticket);
                Assert.DoesNotContain('=', ticket);
            }
        }

        [Fact]
        public void Expired_tickets_do_not_accumulate()
        {
            var service = new WebSocketTicketService();
            for (var i = 0; i < 50; i++)
            {
                service.Issue("device-a", Now);
            }
            Assert.Equal(50, service.Outstanding);

            // Issuing later prunes what has aged out rather than growing forever.
            service.Issue("device-a", Now + WebSocketTicketService.Lifetime + TimeSpan.FromSeconds(1));

            Assert.Equal(1, service.Outstanding);
        }

        [Fact]
        public void Outstanding_tickets_stay_bounded_even_if_none_are_redeemed()
        {
            var service = new WebSocketTicketService();
            for (var i = 0; i < WebSocketTicketService.MaxOutstanding + 200; i++)
            {
                service.Issue("device-a", Now);
            }

            Assert.True(
                service.Outstanding <= WebSocketTicketService.MaxOutstanding,
                $"expected the table to stay bounded, saw {service.Outstanding}");
        }

        [Fact]
        public void A_freshly_issued_ticket_survives_pressure_from_older_ones()
        {
            // Eviction drops the oldest, so a client reconnecting right now must not lose
            // its ticket to a backlog of stale ones.
            var service = new WebSocketTicketService();
            for (var i = 0; i < WebSocketTicketService.MaxOutstanding; i++)
            {
                service.Issue("noisy-device", Now);
            }

            var mine = service.Issue("device-a", Now + TimeSpan.FromSeconds(1));

            Assert.True(service.TryRedeem(mine, Now + TimeSpan.FromSeconds(1), out var deviceId));
            Assert.Equal("device-a", deviceId);
        }
    }
}
