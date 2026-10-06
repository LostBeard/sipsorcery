//-----------------------------------------------------------------------------
// Filename: SctpForwardTsnChunk.cs
//
// Description: Represents the SCTP FORWARD TSN chunk (RFC 3758, partial reliability).
//
// Author(s):
// SpawnDev (MiniRover): added so a peer can skip abandoned chunks on partially reliable
// (maxRetransmits / maxPacketLifeTime) data channels.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Sent by a data sender to tell the receiver to move its cumulative TSN forward past chunks the
    /// sender has abandoned (they will never be retransmitted).
    /// </summary>
    /// <remarks>
    /// https://www.rfc-editor.org/rfc/rfc3758#section-3.2
    ///  0                   1                   2                   3
    ///  0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
    /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    /// |   Type = 192  |  Flags = 0x00 |        Length = Variable      |
    /// |                      New Cumulative TSN                       |
    /// |         Stream-1              |       Stream Sequence-1       |
    /// |         ...                   |       ...                     |
    /// </remarks>
    public class SctpForwardTsnChunk : SctpChunk
    {
        public const int FIXED_PARAMETERS_LENGTH = 4;

        /// <summary>The receiver moves its cumulative TSN to this value, treating every TSN up to it as received.</summary>
        public uint NewCumulativeTSN;

        /// <summary>For ordered streams: the largest stream sequence number being skipped, per stream.</summary>
        public List<(ushort StreamID, ushort StreamSeqNum)> Streams = new List<(ushort, ushort)>();

        private SctpForwardTsnChunk() : base(SctpChunkType.FORWARDTSN)
        { }

        public SctpForwardTsnChunk(uint newCumulativeTsn) : base(SctpChunkType.FORWARDTSN)
        {
            NewCumulativeTSN = newCumulativeTsn;
        }

        public override ushort GetChunkLength(bool padded)
        {
            return (ushort)(SCTP_CHUNK_HEADER_LENGTH + FIXED_PARAMETERS_LENGTH + Streams.Count * 4);
        }

        public override ushort WriteTo(byte[] buffer, int posn)
        {
            WriteChunkHeader(buffer, posn);
            int p = posn + SCTP_CHUNK_HEADER_LENGTH;
            NetConvert.ToBuffer(NewCumulativeTSN, buffer, p);
            p += 4;
            foreach (var (streamID, seq) in Streams)
            {
                NetConvert.ToBuffer(streamID, buffer, p);
                NetConvert.ToBuffer(seq, buffer, p + 2);
                p += 4;
            }
            return GetChunkLength(true);
        }

        public static SctpForwardTsnChunk ParseChunk(byte[] buffer, int posn)
        {
            var chunk = new SctpForwardTsnChunk();
            ushort chunkLen = chunk.ParseFirstWord(buffer, posn);
            chunk.NewCumulativeTSN = NetConvert.ParseUInt32(buffer, posn + SCTP_CHUNK_HEADER_LENGTH);
            int p = posn + SCTP_CHUNK_HEADER_LENGTH + FIXED_PARAMETERS_LENGTH;
            int end = posn + chunkLen;
            while (p + 4 <= end && p + 4 <= buffer.Length)
            {
                chunk.Streams.Add((NetConvert.ParseUInt16(buffer, p), NetConvert.ParseUInt16(buffer, p + 2)));
                p += 4;
            }
            return chunk;
        }
    }
}
