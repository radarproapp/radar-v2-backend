namespace RadarV2.Services;

/// <summary>
/// The model's reply stopped before it was finished.
/// </summary>
/// <remarks>
/// Two ways that happens and neither raised anything: the connection ended without the provider
/// sending its [DONE], and the reply ran into its token ceiling. Both left the streaming loop
/// the same way a completed reply does, so everything downstream treated a fragment as the whole
/// answer - and now that conversations are stored, a sentence cut off in the middle was being
/// written down as what the tutor said and handed back to the model next turn as something it
/// had finished saying. That is exactly the harm the storage was built to avoid.
///
/// Thrown after the text has been yielded, not instead of it. What arrived is what the reader
/// already saw; this says only that there was meant to be more.
/// </remarks>
public sealed class ReplyTruncatedException(string message) : Exception(message);
