//-----------------------------------------------------------------------------
// Filename: SctpForwardTsnUnitTest.cs
//
// Description: Unit tests for FORWARD TSN (RFC 3758) support: the chunk, the INIT
// parameter and the receiver skipping abandoned chunks.
//
// Author(s):
// SpawnDev (MiniRover)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Linq;
using SIPSorcery.Sys;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    public class SctpForwardTsnUnitTest
    {
        public SctpForwardTsnUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        static SctpDataChunk Single(uint tsn, byte b, bool unordered = true, ushort stream = 0, ushort seq = 0) =>
            new SctpDataChunk(unordered, true, true, tsn, stream, seq, 0, new byte[] { b });

        /// <summary>
        /// A lost chunk on a no-retransmit channel used to freeze the cumulative TSN forever. FORWARD TSN moves it past
        /// the hole (and past chunks already received after it), and the stale fragment of the abandoned message goes.
        /// </summary>
        [Fact]
        public void ForwardTsnSkipsALostChunk()
        {
            var receiver = new SctpDataReceiver(0, 0, 0);
            Assert.Single(receiver.OnDataChunk(Single(0, 0x00)));
            // TSN 1 (first half of a two-chunk message) is lost; its end fragment (2) and a whole message (3) arrive.
            Assert.Empty(receiver.OnDataChunk(new SctpDataChunk(true, false, true, 2, 0, 0, 0, new byte[] { 0x02 })));
            Assert.Single(receiver.OnDataChunk(Single(3, 0x03)));
            Assert.Equal(0U, receiver.CumulativeAckTSN);
            Assert.Equal(2, receiver.ForwardTSNCount);

            receiver.OnForwardTsn(new SctpForwardTsnChunk(2));

            Assert.Equal(3U, receiver.CumulativeAckTSN); // 1-2 skipped, 3 was already here
            Assert.Equal(0, receiver.ForwardTSNCount);
            Assert.Single(receiver.OnDataChunk(Single(4, 0x04)));
            Assert.Equal(4U, receiver.CumulativeAckTSN);
            Assert.Empty(receiver.GetSackChunk().GapAckBlocks);
        }

        /// <summary>
        /// Before FORWARD TSN support, once a stream ran more than the receive window past a hole every new chunk was
        /// ignored (this ended desktop sessions with a car streaming video). After the skip, far-ahead TSNs are fine.
        /// </summary>
        [Fact]
        public void ChunksFarBeyondTheOldHoleAreAcceptedAfterForwardTsn()
        {
            var receiver = new SctpDataReceiver(0, 0, 0);
            receiver.OnDataChunk(Single(0, 0x00));
            // TSN 1 lost.
            for (uint tsn = 2; tsn <= 50; tsn++) receiver.OnDataChunk(Single(tsn, (byte)tsn));
            Assert.Equal(0U, receiver.CumulativeAckTSN);
            Assert.Empty(receiver.OnDataChunk(Single(500, 0xFF))); // outside the window measured from the hole: ignored

            receiver.OnForwardTsn(new SctpForwardTsnChunk(1));
            Assert.Equal(50U, receiver.CumulativeAckTSN);
            for (uint tsn = 51; tsn <= 500; tsn++)
            {
                Assert.Single(receiver.OnDataChunk(Single(tsn, (byte)tsn)));
            }
            Assert.Equal(500U, receiver.CumulativeAckTSN);
        }

        [Fact]
        public void OldOrRepeatedForwardTsnIsIgnored()
        {
            var receiver = new SctpDataReceiver(0, 0, 0);
            for (uint tsn = 0; tsn <= 5; tsn++) receiver.OnDataChunk(Single(tsn, 0));
            receiver.OnForwardTsn(new SctpForwardTsnChunk(3));
            Assert.Equal(5U, receiver.CumulativeAckTSN);
            receiver.OnForwardTsn(new SctpForwardTsnChunk(5));
            Assert.Equal(5U, receiver.CumulativeAckTSN);
        }

        [Fact]
        public void ForwardTsnBeforeAnyInOrderChunkStartsTheSequence()
        {
            // The association's first chunk (initial TSN 10) is lost and abandoned.
            var receiver = new SctpDataReceiver(0, 0, 10);
            Assert.Single(receiver.OnDataChunk(Single(11, 0x11)));
            Assert.Null(receiver.CumulativeAckTSN);
            receiver.OnForwardTsn(new SctpForwardTsnChunk(10));
            Assert.Equal(11U, receiver.CumulativeAckTSN);
        }

        /// <summary>Ordered stream: the skipped sequence number no longer blocks the frames queued behind it.</summary>
        [Fact]
        public void OrderedStreamFramesAfterASkippedSequenceAreReleased()
        {
            var receiver = new SctpDataReceiver(0, 0, 0);
            Assert.Single(receiver.OnDataChunk(Single(0, 0xA0, false, 1, 0)));
            // seq 1 (TSN 1) lost; seq 2 (TSN 2) waits for it.
            Assert.Empty(receiver.OnDataChunk(Single(2, 0xA2, false, 1, 2)));

            var frames = receiver.OnForwardTsn(new SctpForwardTsnChunk(1) { Streams = { (1, 1) } });

            Assert.Single(frames);
            Assert.Equal("a2", frames.Single().UserData.HexStr().ToLowerInvariant());
            Assert.Equal(2U, receiver.CumulativeAckTSN);
        }

        [Fact]
        public void ChunkRoundTrips()
        {
            var chunk = new SctpForwardTsnChunk(0xDEADBEEF) { Streams = { (3, 7), (5, 65535) } };
            byte[] buffer = new byte[chunk.GetChunkLength(true)];
            chunk.WriteTo(buffer, 0);
            Assert.Equal(192, buffer[0]);

            var parsed = SctpChunk.Parse(buffer, 0) as SctpForwardTsnChunk;
            Assert.NotNull(parsed);
            Assert.Equal(0xDEADBEEFU, parsed.NewCumulativeTSN);
            Assert.Equal(new[] { ((ushort)3, (ushort)7), ((ushort)5, (ushort)65535) }, parsed.Streams.ToArray());
        }

        [Fact]
        public void InitAdvertisesAndParsesForwardTsnSupport()
        {
            var init = new SctpInitChunk(SctpChunkType.INIT, 1234, 5678, 131072, 1024, 1024) { ForwardTsnSupported = true };
            byte[] buffer = new byte[init.GetChunkLength(true)];
            init.WriteTo(buffer, 0);

            var parsed = SctpChunk.Parse(buffer, 0) as SctpInitChunk;
            Assert.True(parsed.ForwardTsnSupported);
            Assert.Empty(parsed.UnrecognizedPeerParameters);

            var plain = new SctpInitChunk(SctpChunkType.INIT, 1234, 5678, 131072, 1024, 1024);
            buffer = new byte[plain.GetChunkLength(true)];
            plain.WriteTo(buffer, 0);
            Assert.False((SctpChunk.Parse(buffer, 0) as SctpInitChunk).ForwardTsnSupported);
        }
    }
}
