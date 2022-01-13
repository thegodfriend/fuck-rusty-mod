﻿using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// WAV utility for recording and audio playback functions in Unity.
/// Version: 1.0 alpha 1
///
/// - Use "ToAudioClip" method for loading wav file / bytes.
/// Loads .wav (PCM uncompressed) files at 8,16,24 and 32 bits and converts data to Unity's AudioClip.
///
/// - Use "FromAudioClip" method for saving wav file / bytes.
/// Converts an AudioClip's float data into wav byte array at 16 bit.
/// </summary>
/// <remarks>
/// For documentation and usage examples: https://github.com/deadlyfingers/UnityWav
/// </remarks>
namespace FuckRusty
{
	public class WavUtility
	{
		// Force save as 16-bit .wav
		private const int BlockSize16Bit = 2;

		/// <summary>
		///     Load PCM format *.wav audio file (using Unity's Application data path) and convert to AudioClip.
		/// </summary>
		/// <returns>The AudioClip.</returns>
		/// <param name="filePath">Local file path to .wav file</param>
		public static AudioClip ToAudioClip(string filePath)
		{
			var fileBytes = File.ReadAllBytes(filePath);
			return ToAudioClip(fileBytes);
		}

		public static AudioClip ToAudioClip(byte[] fileBytes, int offsetSamples = 0, string name = "wav")
		{
			//string riff = Encoding.ASCII.GetString (fileBytes, 0, 4);
			//string wave = Encoding.ASCII.GetString (fileBytes, 8, 4);
			var subchunk1 = BitConverter.ToInt32(fileBytes, 16);
			var audioFormat = BitConverter.ToUInt16(fileBytes, 20);

			// NB: Only uncompressed PCM wav files are supported.
			var formatCode = FormatCode(audioFormat);
			Debug.AssertFormat(audioFormat == 1 || audioFormat == 65534,
				"Detected format code '{0}' {1}, but only PCM and WaveFormatExtensable uncompressed formats are currently supported.",
				audioFormat, formatCode);

			var channels = BitConverter.ToUInt16(fileBytes, 22);
			var sampleRate = BitConverter.ToInt32(fileBytes, 24);
			//int byteRate = BitConverter.ToInt32 (fileBytes, 28);
			//UInt16 blockAlign = BitConverter.ToUInt16 (fileBytes, 32);
			var bitDepth = BitConverter.ToUInt16(fileBytes, 34);

			var headerOffset = 16 + 4 + subchunk1 + 4;
			var subchunk2 = BitConverter.ToInt32(fileBytes, headerOffset);
			//Debug.LogFormat ("riff={0} wave={1} subchunk1={2} format={3} channels={4} sampleRate={5} byteRate={6} blockAlign={7} bitDepth={8} headerOffset={9} subchunk2={10} filesize={11}", riff, wave, subchunk1, formatCode, channels, sampleRate, byteRate, blockAlign, bitDepth, headerOffset, subchunk2, fileBytes.Length);

			float[] data;
			switch (bitDepth)
			{
				case 8:
					data = Convert8BitByteArrayToAudioClipData(fileBytes, headerOffset, subchunk2);
					break;
				case 16:
					data = Convert16BitByteArrayToAudioClipData(fileBytes, headerOffset, subchunk2);
					break;
				case 24:
					data = Convert24BitByteArrayToAudioClipData(fileBytes, headerOffset, subchunk2);
					break;
				case 32:
					data = Convert32BitByteArrayToAudioClipData(fileBytes, headerOffset, subchunk2);
					break;
				default:
					throw new Exception(bitDepth + " bit depth is not supported.");
			}

			var audioClip = AudioClip.Create(name, data.Length / channels, channels, sampleRate, false);
			audioClip.SetData(data, 0);
			return audioClip;
		}

		public static byte[] FromAudioClip(AudioClip audioClip)
		{
			string file;
			return FromAudioClip(audioClip, out file, false);
		}

		public static byte[] FromAudioClip(AudioClip audioClip, out string filepath, bool saveAsFile = true,
			string dirname = "recordings")
		{
			var stream = new MemoryStream();

			const int headerSize = 44;

			// get bit depth
			ushort bitDepth = 16; //BitDepth (audioClip);

			// NB: Only supports 16 bit
			//Debug.AssertFormat (bitDepth == 16, "Only converting 16 bit is currently supported. The audio clip data is {0} bit.", bitDepth);

			// total file size = 44 bytes for header format and audioClip.samples * factor due to float to Int16 / sbyte conversion
			var fileSize = audioClip.samples * BlockSize16Bit + headerSize; // BlockSize (bitDepth)

			// chunk descriptor (riff)
			WriteFileHeader(ref stream, fileSize);
			// file header (fmt)
			WriteFileFormat(ref stream, audioClip.channels, audioClip.frequency, bitDepth);
			// data chunks (data)
			WriteFileData(ref stream, audioClip, bitDepth);

			var bytes = stream.ToArray();

			// Validate total bytes
			Debug.AssertFormat(bytes.Length == fileSize, "Unexpected AudioClip to wav format byte count: {0} == {1}",
				bytes.Length, fileSize);

			// Save file to persistant storage location
			if (saveAsFile)
			{
				filepath = string.Format("{0}/{1}/{2}.{3}", Application.persistentDataPath, dirname,
					DateTime.UtcNow.ToString("yyMMdd-HHmmss-fff"), "wav");
				Directory.CreateDirectory(Path.GetDirectoryName(filepath));
				File.WriteAllBytes(filepath, bytes);
				//Debug.Log ("Auto-saved .wav file: " + filepath);
			}
			else
			{
				filepath = null;
			}

			stream.Dispose();

			return bytes;
		}

		/// <summary>
		///     Calculates the bit depth of an AudioClip
		/// </summary>
		/// <returns>The bit depth. Should be 8 or 16 or 32 bit.</returns>
		/// <param name="audioClip">Audio clip.</param>
		public static ushort BitDepth(AudioClip audioClip)
		{
			var bitDepth =
				Convert.ToUInt16(audioClip.samples * audioClip.channels * audioClip.length / audioClip.frequency);
			Debug.AssertFormat(bitDepth == 8 || bitDepth == 16 || bitDepth == 32,
				"Unexpected AudioClip bit depth: {0}. Expected 8 or 16 or 32 bit.", bitDepth);
			return bitDepth;
		}

		private static int BytesPerSample(ushort bitDepth)
		{
			return bitDepth / 8;
		}

		private static int BlockSize(ushort bitDepth)
		{
			switch (bitDepth)
			{
				case 32:
					return sizeof(int); // 32-bit -> 4 bytes (Int32)
				case 16:
					return sizeof(short); // 16-bit -> 2 bytes (Int16)
				case 8:
					return sizeof(sbyte); // 8-bit -> 1 byte (sbyte)
				default:
					throw new Exception(bitDepth + " bit depth is not supported.");
			}
		}

		private static string FormatCode(ushort code)
		{
			switch (code)
			{
				case 1:
					return "PCM";
				case 2:
					return "ADPCM";
				case 3:
					return "IEEE";
				case 7:
					return "μ-law";
				case 65534:
					return "WaveFormatExtensable";
				default:
					Debug.LogWarning("Unknown wav code format:" + code);
					return "";
			}
		}

		#region wav file bytes to Unity AudioClip conversion methods

		private static float[] Convert8BitByteArrayToAudioClipData(byte[] source, int headerOffset, int dataSize)
		{
			var wavSize = BitConverter.ToInt32(source, headerOffset);
			headerOffset += sizeof(int);
			Debug.AssertFormat(wavSize > 0 && wavSize == dataSize,
				"Failed to get valid 8-bit wav size: {0} from data bytes: {1} at offset: {2}", wavSize, dataSize,
				headerOffset);

			var data = new float[wavSize];

			var maxValue = sbyte.MaxValue;

			var i = 0;
			while (i < wavSize)
			{
				data[i] = (float)source[i] / maxValue;
				++i;
			}

			return data;
		}

		private static float[] Convert16BitByteArrayToAudioClipData(byte[] source, int headerOffset, int dataSize)
		{
			var wavSize = BitConverter.ToInt32(source, headerOffset);
			headerOffset += sizeof(int);
			Debug.AssertFormat(wavSize > 0 && wavSize == dataSize,
				"Failed to get valid 16-bit wav size: {0} from data bytes: {1} at offset: {2}", wavSize, dataSize,
				headerOffset);

			var x = sizeof(short); // block size = 2
			var convertedSize = wavSize / x;

			var data = new float[convertedSize];

			var maxValue = short.MaxValue;

			var offset = 0;
			var i = 0;
			while (i < convertedSize)
			{
				offset = i * x + headerOffset;
				data[i] = (float)BitConverter.ToInt16(source, offset) / maxValue;
				++i;
			}

			Debug.AssertFormat(data.Length == convertedSize, "AudioClip .wav data is wrong size: {0} == {1}",
				data.Length, convertedSize);

			return data;
		}

		private static float[] Convert24BitByteArrayToAudioClipData(byte[] source, int headerOffset, int dataSize)
		{
			var wavSize = BitConverter.ToInt32(source, headerOffset);
			headerOffset += sizeof(int);
			Debug.AssertFormat(wavSize > 0 && wavSize == dataSize,
				"Failed to get valid 24-bit wav size: {0} from data bytes: {1} at offset: {2}", wavSize, dataSize,
				headerOffset);

			var x = 3; // block size = 3
			var convertedSize = wavSize / x;

			var maxValue = int.MaxValue;

			var data = new float[convertedSize];

			var
				block = new byte[sizeof(int)]; // using a 4 byte block for copying 3 bytes, then copy bytes with 1 offset

			var offset = 0;
			var i = 0;
			while (i < convertedSize)
			{
				offset = i * x + headerOffset;
				Buffer.BlockCopy(source, offset, block, 1, x);
				data[i] = (float)BitConverter.ToInt32(block, 0) / maxValue;
				++i;
			}

			Debug.AssertFormat(data.Length == convertedSize, "AudioClip .wav data is wrong size: {0} == {1}",
				data.Length, convertedSize);

			return data;
		}

		private static float[] Convert32BitByteArrayToAudioClipData(byte[] source, int headerOffset, int dataSize)
		{
			var wavSize = BitConverter.ToInt32(source, headerOffset);
			headerOffset += sizeof(int);
			Debug.AssertFormat(wavSize > 0 && wavSize == dataSize,
				"Failed to get valid 32-bit wav size: {0} from data bytes: {1} at offset: {2}", wavSize, dataSize,
				headerOffset);

			var x = sizeof(float); //  block size = 4
			var convertedSize = wavSize / x;

			var maxValue = int.MaxValue;

			var data = new float[convertedSize];

			var offset = 0;
			var i = 0;
			while (i < convertedSize)
			{
				offset = i * x + headerOffset;
				data[i] = (float)BitConverter.ToInt32(source, offset) / maxValue;
				++i;
			}

			Debug.AssertFormat(data.Length == convertedSize, "AudioClip .wav data is wrong size: {0} == {1}",
				data.Length, convertedSize);

			return data;
		}

		#endregion

		#region write .wav file functions

		private static int WriteFileHeader(ref MemoryStream stream, int fileSize)
		{
			var count = 0;
			var total = 12;

			// riff chunk id
			var riff = Encoding.ASCII.GetBytes("RIFF");
			count += WriteBytesToMemoryStream(ref stream, riff, "ID");

			// riff chunk size
			var chunkSize = fileSize - 8; // total size - 8 for the other two fields in the header
			count += WriteBytesToMemoryStream(ref stream, BitConverter.GetBytes(chunkSize), "CHUNK_SIZE");

			var wave = Encoding.ASCII.GetBytes("WAVE");
			count += WriteBytesToMemoryStream(ref stream, wave, "FORMAT");

			// Validate header
			Debug.AssertFormat(count == total, "Unexpected wav descriptor byte count: {0} == {1}", count, total);

			return count;
		}

		private static int WriteFileFormat(ref MemoryStream stream, int channels, int sampleRate, ushort bitDepth)
		{
			var count = 0;
			var total = 24;

			var id = Encoding.ASCII.GetBytes("fmt ");
			count += WriteBytesToMemoryStream(ref stream, id, "FMT_ID");

			var subchunk1Size = 16; // 24 - 8
			count += WriteBytesToMemoryStream(ref stream, BitConverter.GetBytes(subchunk1Size), "SUBCHUNK_SIZE");

			ushort audioFormat = 1;
			count += WriteBytesToMemoryStream(ref stream, BitConverter.GetBytes(audioFormat), "AUDIO_FORMAT");

			var numChannels = Convert.ToUInt16(channels);
			count += WriteBytesToMemoryStream(ref stream, BitConverter.GetBytes(numChannels), "CHANNELS");

			count += WriteBytesToMemoryStream(ref stream, BitConverter.GetBytes(sampleRate), "SAMPLE_RATE");

			var byteRate = sampleRate * channels * BytesPerSample(bitDepth);
			count += WriteBytesToMemoryStream(ref stream, BitConverter.GetBytes(byteRate), "BYTE_RATE");

			var blockAlign = Convert.ToUInt16(channels * BytesPerSample(bitDepth));
			count += WriteBytesToMemoryStream(ref stream, BitConverter.GetBytes(blockAlign), "BLOCK_ALIGN");

			count += WriteBytesToMemoryStream(ref stream, BitConverter.GetBytes(bitDepth), "BITS_PER_SAMPLE");

			// Validate format
			Debug.AssertFormat(count == total, "Unexpected wav fmt byte count: {0} == {1}", count, total);

			return count;
		}

		private static int WriteFileData(ref MemoryStream stream, AudioClip audioClip, ushort bitDepth)
		{
			var count = 0;
			var total = 8;

			// Copy float[] data from AudioClip
			var data = new float[audioClip.samples * audioClip.channels];
			audioClip.GetData(data, 0);

			var bytes = ConvertAudioClipDataToInt16ByteArray(data);

			var id = Encoding.ASCII.GetBytes("data");
			count += WriteBytesToMemoryStream(ref stream, id, "DATA_ID");

			var subchunk2Size = Convert.ToInt32(audioClip.samples * BlockSize16Bit); // BlockSize (bitDepth)
			count += WriteBytesToMemoryStream(ref stream, BitConverter.GetBytes(subchunk2Size), "SAMPLES");

			// Validate header
			Debug.AssertFormat(count == total, "Unexpected wav data id byte count: {0} == {1}", count, total);

			// Write bytes to stream
			count += WriteBytesToMemoryStream(ref stream, bytes, "DATA");

			// Validate audio data
			Debug.AssertFormat(bytes.Length == subchunk2Size, "Unexpected AudioClip to wav subchunk2 size: {0} == {1}",
				bytes.Length, subchunk2Size);

			return count;
		}

		private static byte[] ConvertAudioClipDataToInt16ByteArray(float[] data)
		{
			var dataStream = new MemoryStream();

			var x = sizeof(short);

			var maxValue = short.MaxValue;

			var i = 0;
			while (i < data.Length)
			{
				dataStream.Write(BitConverter.GetBytes(Convert.ToInt16(data[i] * maxValue)), 0, x);
				++i;
			}

			var bytes = dataStream.ToArray();

			// Validate converted bytes
			Debug.AssertFormat(data.Length * x == bytes.Length,
				"Unexpected float[] to Int16 to byte[] size: {0} == {1}", data.Length * x, bytes.Length);

			dataStream.Dispose();

			return bytes;
		}

		private static int WriteBytesToMemoryStream(ref MemoryStream stream, byte[] bytes, string tag = "")
		{
			var count = bytes.Length;
			stream.Write(bytes, 0, count);
			//Debug.LogFormat ("WAV:{0} wrote {1} bytes.", tag, count);
			return count;
		}

		#endregion
	}
}