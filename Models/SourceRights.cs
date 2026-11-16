namespace RadarV2.Models;

/// <summary>
/// How a source may be reached, and what Radar is permitted to do with what it returns.
/// </summary>
/// <remarks>
/// Recorded per source because the brief is emphatic and the codebase had nowhere to put it:
/// "Free website or public portal does not automatically grant commercial redistribution
/// rights." Six RSS feeds are being ingested and shown today under an assumption nobody wrote
/// down, let alone checked.
///
/// A field that must be filled in makes the omission visible. <see cref="Redistribution"/> has
/// no default for that reason - adding a source without saying what may be done with it should
/// be something you notice writing the line, not something discovered by a letter.
/// </remarks>
public enum SourceAccess
{
    /// <summary>A documented API, free to call. Preferred.</summary>
    FreeApi,

    /// <summary>An official RSS or Atom feed.</summary>
    Feed,

    /// <summary>An official site with structured listings, and no API confirmed.</summary>
    PublicPortal,

    /// <summary>Behind a subscription, agreement or partnership.</summary>
    Negotiated,
}

/// <summary>
/// What Radar may show from a source.
/// </summary>
/// <remarks>
/// The three cases differ in what reaches a user, so this is not paperwork. A source we may only
/// link to must never have its description stored and rendered as though Radar wrote it.
/// </remarks>
public enum Redistribution
{
    /// <summary>
    /// Nobody has checked.
    /// </summary>
    /// <remarks>
    /// The honest default for a source added without the question being asked, and deliberately
    /// the first value so an uninitialised field reads as "unknown" rather than as permission.
    /// </remarks>
    Unconfirmed,

    /// <summary>Terms permit storing and showing the listing, with attribution.</summary>
    Permitted,

    /// <summary>Title and link only. The description stays on their site.</summary>
    LinkOnly,

    /// <summary>Monitor for discovery, publish nothing, until terms are agreed.</summary>
    DoNotPublish,
}
