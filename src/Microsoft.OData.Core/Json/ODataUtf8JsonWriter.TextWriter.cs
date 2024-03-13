//---------------------------------------------------------------------
// <copyright file="ODataUtf8JsonWriter.TextWriter.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------

#if NETCOREAPP
namespace Microsoft.OData.Json
{
    using System;
    using System.Buffers;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;

    internal sealed partial class ODataUtf8JsonWriter
    {
        /// <summary>
        /// Starts a TextWriter value scope with the specified content type.
        /// </summary>
        /// <param name="contentType">The content type of the TextWriter value scope.</param>
        /// <returns>A TextWriter for writing JSON data.</returns>
        public TextWriter StartTextWriterValueScope(string contentType)
        {
            this.WriteSeparatorIfNecessary();
            this.currentContentType = contentType;
            if (!IsWritingJson)
            {
                this.bufferWriter.Write(this.DoubleQuote.Slice(0, 1).Span);
            }

            this.Flush();

            return new ODataUtf8JsonTextWriter(this);
        }

        /// <summary>
        /// Ends a TextWriter value scope.
        /// </summary>
        public void EndTextWriterValueScope()
        {
            if (!IsWritingJson)
            {
                this.bufferWriter.Write(this.DoubleQuote.Slice(0, 1).Span);
                this.Flush();
            }
        }

        /// <summary>
        /// Asynchronously starts a TextWriter value scope with the specified content type.
        /// </summary>
        /// <param name="contentType">The content type of the TextWriter value scope.</param>
        /// <returns>A task representing the asynchronous operation. The task result is a TextWriter for writing JSON data.</returns>
        public async Task<TextWriter> StartTextWriterValueScopeAsync(string contentType)
        {
            this.WriteSeparatorIfNecessary();
            this.currentContentType = contentType;
            if (!IsWritingJson)
            {
                this.bufferWriter.Write(this.DoubleQuote.Slice(0, 1).Span);
            }

            await this.FlushIfBufferThresholdReachedAsync().ConfigureAwait(false);

            return new ODataUtf8JsonTextWriter(this);
        }

        /// <summary>
        /// Asynchronously ends a TextWriter value scope.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task EndTextWriterValueScopeAsync()
        {
            if (!IsWritingJson)
            {
                this.bufferWriter.Write(this.DoubleQuote.Slice(0, 1).Span);
                await this.FlushIfBufferThresholdReachedAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Whether the current TextWriter is writing JSON
        /// </summary>
        /// <returns></returns>
        private bool IsWritingJson
        {
            get
            {
                return String.IsNullOrEmpty(this.currentContentType) || this.currentContentType.StartsWith(MimeConstants.MimeApplicationJson, StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// Represents a TextWriter implementation for writing UTF-8 JSON data in an OData context.
        /// </summary>
        internal class ODataUtf8JsonTextWriter : TextWriter
        {
            ODataUtf8JsonWriter jsonWriter = null;
            private char[] buffer;
            private int bufferPosition = 0;
            int charsNotWrittenFromPreviousChunk = 0;

            public ODataUtf8JsonTextWriter(ODataUtf8JsonWriter jsonWriter)
            {
                this.jsonWriter = jsonWriter;
                buffer = new char[chunkSize];
            }

            public override Encoding Encoding => throw new NotImplementedException();

            /// <summary>
            /// Flushes any buffered data to the underlying stream synchronously.
            /// </summary>
            public override void Flush()
            {
                // If there are unprocessed chars, encode and write them as the final block.
                ReadOnlySpan<char> charsNotProcessedFromPreviousChunk = this.buffer.AsSpan().Slice(bufferPosition - charsNotWrittenFromPreviousChunk, charsNotWrittenFromPreviousChunk);

                if (!charsNotProcessedFromPreviousChunk.IsEmpty)
                {
                    int firstIndexToEscape = this.jsonWriter.NeedsEscaping(charsNotProcessedFromPreviousChunk);
                    WriteChunk(charsNotProcessedFromPreviousChunk, charsNotProcessedFromPreviousChunk.Length, isFinalBlock: true, firstIndexToEscape);

                    // Clear the buffer since all bytes have been written.
                    this.buffer = null;
                    this.bufferPosition = 0;
                    this.charsNotWrittenFromPreviousChunk = 0;
                }

                this.jsonWriter.Flush();
            }

            /// <summary>
            /// Flushes any buffered data to the underlying stream asynchronously.
            /// </summary>
            /// <param name="cancellationToken">A cancellation token to observe while waiting for the flush operation to complete.</param>
            /// <returns>A task representing the asynchronous flush operation.</returns>
            public override async Task FlushAsync()
            {
                // If there are unprocessed chars, encode and write them as the final block.
                ReadOnlyMemory<char> charsNotProcessedFromPreviousChunk = this.buffer.AsMemory().Slice(bufferPosition - charsNotWrittenFromPreviousChunk, charsNotWrittenFromPreviousChunk);

                if (!charsNotProcessedFromPreviousChunk.IsEmpty)
                {
                    int firstIndexToEscape = this.jsonWriter.NeedsEscaping(charsNotProcessedFromPreviousChunk.Span);
                    WriteChunk(charsNotProcessedFromPreviousChunk.Span, charsNotProcessedFromPreviousChunk.Length, isFinalBlock: true, firstIndexToEscape);

                    // Clear the buffer since all bytes have been written.
                    this.buffer = null;
                    this.bufferPosition = 0;
                    this.charsNotWrittenFromPreviousChunk = 0;
                }

                await this.jsonWriter.FlushAsync();
            }

            /// <summary>
            /// Writes a specified number of characters from the given character array to the ODataUtf8JsonWriter.
            /// </summary>
            /// <param name="value">The character array from which to write characters.</param>
            /// <param name="index">The starting index in the character array from which to begin writing characters.</param>
            /// <param name="count">The number of characters to write from the character array.</param>
            public override void Write(char[] value, int index, int count)
            {
                ReadOnlySpan<char> charValuesToWrite = value.AsSpan().Slice(index, count);
                this.StartWritingCharValuesInChunks(charValuesToWrite);
            }

            /// <summary>
            /// Asynchronously writes a specified number of characters from the given character array to theODataUtf8JsonWriter.
            /// </summary>
            /// <param name="value">The character array from which to write characters.</param>
            /// <param name="index">The starting index in the character array from which to begin writing characters.</param>
            /// <param name="count">The number of characters to write from the character array.</param>
            /// <returns>A task representing the asynchronous write operation.</returns>
            public override async Task WriteAsync(char[] value, int index, int count)
            {
                ReadOnlyMemory<char> charValuesToWrite = buffer.AsMemory().Slice(index, count);
                await this.StartWritingCharValuesInChunksAsync(charValuesToWrite);
            }

            /// <summary>
            /// Writes the characters represented by the provided read-only span in chunks, processing them for escaping if necessary, and flushing the writer if the buffer threshold is reached.
            /// </summary>
            /// <param name="value">The read-only span containing the characters to be written.</param>
            private void StartWritingCharValuesInChunks(ReadOnlySpan<char> value)
            {
                // Process the input value in chunks
                for (int i = 0; i < value.Length; i += chunkSize)
                {
                    int remainingChars = Math.Min(chunkSize, value.Length - i);
                    bool isFinalBlock = false;
                    ReadOnlySpan<char> chunk = value.Slice(i, remainingChars);

                    int firstIndexToEscape = this.jsonWriter.NeedsEscaping(chunk);

                    // If the buffer is not empty, then we copy the chars from the buffer
                    // to the current chunk being processed.
                    if (this.buffer != null)
                    {
                        // Get unprocessed chars from the buffer.
                        ReadOnlySpan<char> charsNotProcessedFromPreviousChunk = this.buffer.AsSpan().Slice(bufferPosition - charsNotWrittenFromPreviousChunk, charsNotWrittenFromPreviousChunk);
                        int totalLength = charsNotProcessedFromPreviousChunk.Length + chunk.Length;

                        char[] combinedArray = ArrayPool<char>.Shared.Rent(totalLength);

                        // Copy chars from charsNotProcessedFromPreviousChunk to the combined array
                        charsNotProcessedFromPreviousChunk.CopyTo(combinedArray);

                        // Copy chars from chunk to the combined array, starting from the end of charsNotProcessedFromPreviousChunk
                        chunk.CopyTo(combinedArray.AsSpan().Slice(charsNotProcessedFromPreviousChunk.Length));

                        // Write the chunk depending on the firstIndexToEscape
                        WriteChunk(combinedArray, totalLength, isFinalBlock, firstIndexToEscape);

                        if (combinedArray != null)
                        {
                            ArrayPool<char>.Shared.Return(combinedArray);
                        }
                    }
                    else
                    {
                        // Write the chunk directly depending on the firstIndexToEscape
                        WriteChunk(chunk, chunk.Length, isFinalBlock, firstIndexToEscape);
                    }

                    // Flush the writer if the buffer threshold is reached
                    this.jsonWriter.FlushIfBufferThresholdReached();
                }
            }

            /// <summary>
            /// Writes character values represented by the provided read-only memory in chunks, processing them for escaping if necessary, and asynchronously flushes the writer if the buffer threshold is reached.
            /// </summary>
            /// <param name="value">The read-only memory containing the character values to be written.</param>
            /// <returns>A ValueTask representing the asynchronous operation.</returns>
            private async ValueTask StartWritingCharValuesInChunksAsync(ReadOnlyMemory<char> value)
            {
                // Process the input value in chunks
                for (int i = 0; i < value.Length; i += chunkSize)
                {
                    int remainingChars = Math.Min(chunkSize, value.Length - i);
                    bool isFinalBlock = false;
                    ReadOnlyMemory<char> chunk = value.Slice(i, remainingChars);

                    int firstIndexToEscape = this.jsonWriter.NeedsEscaping(chunk.Span);

                    // If the buffer is not empty, then we copy the chars from the buffer
                    // to the current chunk being processed.
                    if (this.buffer != null)
                    {
                        // Get unprocessed chars from the buffer.
                        ReadOnlyMemory<char> charsNotProcessedFromPreviousChunk = this.buffer.AsMemory().Slice(bufferPosition - charsNotWrittenFromPreviousChunk, charsNotWrittenFromPreviousChunk);
                        int totalLength = charsNotProcessedFromPreviousChunk.Length + chunk.Length;

                        char[] combinedArray = ArrayPool<char>.Shared.Rent(totalLength);

                        // Copy chars from charsNotProcessedFromPreviousChunk to the combined array
                        charsNotProcessedFromPreviousChunk.CopyTo(combinedArray);

                        // Copy chars from chunk to the combined array, starting from the end of charsNotProcessedFromPreviousChunk
                        chunk.CopyTo(combinedArray.AsMemory().Slice(charsNotProcessedFromPreviousChunk.Length));

                        // Write the chunk depending on the firstIndexToEscape
                        WriteChunk(combinedArray, totalLength, isFinalBlock, firstIndexToEscape);

                        if (combinedArray != null)
                        {
                            ArrayPool<char>.Shared.Return(combinedArray);
                        }
                    }
                    else
                    {
                        // Write the chunk directly depending on the firstIndexToEscape
                        WriteChunk(chunk.Span, chunk.Length, isFinalBlock, firstIndexToEscape);
                    }

                    // Flush the writer if the buffer threshold is reached
                    await this.jsonWriter.FlushIfBufferThresholdReachedAsync().ConfigureAwait(false);
                }
            }

            /// <summary>
            /// Writes a chunk of characters to the JSON writer, either escaped or directly, depending on the presence of characters that need escaping.
            /// </summary>
            /// <param name="chunk">The read-only span containing the chunk of characters to be written.</param>
            /// <param name="chunkLength">The length of the read-only span containing the chunk of characters to be written.</param>
            /// <param name="isFinalBlock">A boolean value indicating whether the current chunk is the final block of characters to be written.</param>
            /// <param name="firstIndexToEscape">The index of the first character in the chunk that requires escaping, or -1 if no characters need escaping.</param>
            private void WriteChunk(ReadOnlySpan<char> chunk, int chunkLength, bool isFinalBlock, int firstIndexToEscape)
            {
                if (firstIndexToEscape != -1)
                {
                    if (this.buffer == null)
                    {
                        this.buffer = new char[chunkSize];
                    }

                    this.jsonWriter.WriteEscapedStringChunk(chunk.Slice(0, chunkLength), firstIndexToEscape, isFinalBlock, out charsNotWrittenFromPreviousChunk);

                    if (charsNotWrittenFromPreviousChunk > 0)
                    {
                        // Update the buffer with unprocessed chars from the current chunk.
                        chunk.Slice(chunkLength - charsNotWrittenFromPreviousChunk).CopyTo(this.buffer.AsSpan(bufferPosition));
                        bufferPosition += charsNotWrittenFromPreviousChunk;
                    }
                }
                else
                {
                    this.jsonWriter.WriteStringChunk(chunk, isFinalBlock);
                }
            }
        }
    }
}
#endif