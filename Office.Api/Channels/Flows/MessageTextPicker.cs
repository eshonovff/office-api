namespace Office.Api.Channels.Flows;

/// <summary>
/// Picks which of a text block's texts (Text, then its Variants) a contact gets. Chosen from the
/// session and the node rather than at random: the same contact at the same node always gets the
/// same text — a retry after the 24-hour window reopens, or a "follow us first" sent again, does
/// not change wording — while different contacts spread evenly (the ids' last bytes are random).
/// </summary>
public static class MessageTextPicker
{
    public static string Pick(MessageBlock block, Guid sessionId, Guid nodeId)
    {
        var variants = block.Variants?.Where(v => !string.IsNullOrWhiteSpace(v)).ToArray() ?? [];
        if (variants.Length == 0)
            return block.Text!;

        var index = (int)((RandomTail(sessionId) ^ RandomTail(nodeId)) % (uint)(variants.Length + 1));
        return index == 0 ? block.Text! : variants[index - 1];
    }

    // Guid v7: the first bytes are the timestamp, the last ones random.
    private static uint RandomTail(Guid id) => BitConverter.ToUInt32(id.ToByteArray(), 12);
}
