namespace RadarV2.Services;

/// <summary>
/// The research index could not be reached, which is not the same as there being no research.
/// </summary>
/// <remarks>
/// A failed OpenAlex call used to be caught and dropped, leaving an empty list that the screen
/// renders as "no research matches your search". So an outage read to the reader as a fact about
/// their topic - that nothing has been published on it - which is both wrong and the kind of
/// confident wrong answer this product is built to avoid. The spec asks that errors be explained
/// and that a failed request can be retried; neither is possible if the failure is invisible.
/// </remarks>
public sealed class ResearchUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
