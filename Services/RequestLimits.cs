using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace RadarV2.Services;

/// <summary>
/// How much of Radar one caller may use at once.
/// </summary>
/// <remarks>
/// There was no limit of any kind. That is survivable while the product is small and becomes the
/// first real problem as it grows, because the expensive things here are not CPU: a path build
/// is several model calls, an assessment is one, and the video search draws on a quota of about
/// a hundred searches a day for the whole product. One client in a retry loop can spend the
/// month's budget before anybody notices, and nothing in the system would have refused it.
///
/// Two buckets, because the costs differ by two orders of magnitude:
///
/// Ordinary reads - the feed, a lesson, an opportunity list - are cheap and frequent, and a
/// learner flicking between screens should never meet a limit.
///
/// Anything that calls a model, or the one metered external API, is neither. A person cannot
/// usefully start six paths in a minute; a loop can.
///
/// Keyed by user where there is one and by address otherwise, so a limit follows the account
/// rather than the network - several learners behind one Nigerian ISP's NAT would otherwise
/// share a budget.
/// </remarks>
public static class RequestLimits
{
    /// <summary>The bucket ordinary reads fall into.</summary>
    public const string Reading = "reading";

    /// <summary>And the one anything expensive falls into.</summary>
    public const string Thinking = "thinking";

    public static IServiceCollection AddRadarRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            // A refusal the caller can act on. The default is 503, which reads as "Radar is
            // broken" rather than "you are going too fast"; 429 with a retry hint is the
            // difference between a client backing off and a client retrying harder.
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, token) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString();
                }

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { error = "That is faster than Radar can answer. Try again shortly." },
                    token);
            };

            options.AddPolicy(Reading, Bucket(permits: 120, perSeconds: 60, queue: 10));
            options.AddPolicy(Thinking, Bucket(permits: 12, perSeconds: 60, queue: 2));
        });

    /// <summary>
    /// A window per caller, with a short queue rather than an immediate refusal.
    /// </summary>
    /// <remarks>
    /// Sliding rather than fixed, because a fixed window lets a caller spend the whole allowance
    /// in the last second of one window and again in the first of the next - twice the intended
    /// rate, at exactly the moment a retry loop is most likely to be doing it.
    ///
    /// The small queue is what keeps a limit from punishing a page that legitimately fires three
    /// requests as it opens.
    /// </remarks>
    private static Func<HttpContext, RateLimitPartition<string>> Bucket(int permits, int perSeconds, int queue) =>
        context => RateLimitPartition.GetSlidingWindowLimiter(
            Caller(context),
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = TimeSpan.FromSeconds(perSeconds),
                SegmentsPerWindow = 6,
                QueueLimit = queue,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            });

    /// <summary>
    /// Who is being limited: the account where there is one, the address otherwise.
    /// </summary>
    /// <remarks>
    /// Signed-in traffic is keyed by user id on purpose. Several learners sharing one mobile
    /// network appear as a single address, and limiting that address would ration a classroom.
    /// </remarks>
    private static string Caller(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? context.User.FindFirstValue("sub")
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";
}
