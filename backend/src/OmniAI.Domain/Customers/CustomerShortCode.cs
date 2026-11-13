namespace OmniAI.Domain.Customers;

/// <summary>
/// A short, stable code that tells unnamed customers apart on screen.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Derived, never stored, and never a key.</strong> Six Crockford base32
/// characters (30 bits) read from the customer's own id, so the same customer has
/// the same code in every list, every export and every session, and nothing has
/// to be backfilled for customers created before it existed. It is a label for
/// people to say out loud ("visitor 7KQ2MX"), not an identifier: two customers
/// of one workspace can share a code, so nothing may look a customer up by it.
/// </para>
/// <para>
/// <strong>From the random end of the id, never its leading characters.</strong>
/// Customer ids are UUIDv7: the first 48 bits are a millisecond timestamp, so
/// customers created close together share their leading characters and a code
/// cut from there would repeat for everybody who arrived in the same minute. The
/// last bytes of a UUIDv7 are random (and so are a UUIDv4's), which is what makes
/// customers created a millisecond apart read differently.
/// </para>
/// </remarks>
public static class CustomerShortCode
{
    /// <summary>The number of characters in a code.</summary>
    public const int Length = 6;

    // Crockford's base32: digits and upper-case letters without I, L, O and U, so
    // a code read aloud or copied by hand cannot be mistaken for another one.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>The code for this customer.</summary>
    public static string For(CustomerId customerId) => For(customerId.Value);

    /// <summary>The code for a customer id.</summary>
    /// <exception cref="DomainValidationException">The id is empty.</exception>
    public static string For(Guid customerId)
    {
        if (customerId == Guid.Empty)
        {
            throw new DomainValidationException("A short code needs a real customer id.")
            {
                Code = "customers.id.invalid",
            };
        }

        // Big-endian, so byte 15 is the last byte of the canonical text form and
        // bytes 12-15 sit wholly inside the random part (rand_b) of a UUIDv7.
        Span<byte> bytes = stackalloc byte[16];
        customerId.TryWriteBytes(bytes, bigEndian: true, out _);

        uint random = ((uint)bytes[12] << 24) | ((uint)bytes[13] << 16) | ((uint)bytes[14] << 8) | bytes[15];

        Span<char> code = stackalloc char[Length];

        for (int index = Length - 1; index >= 0; index--)
        {
            code[index] = Alphabet[(int)(random & 0x1F)];
            random >>= 5;
        }

        return new string(code);
    }
}
