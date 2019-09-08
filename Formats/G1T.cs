﻿using G1Tool.IO;
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using SFGraphics.GLObjects.Textures;
using SFGraphics.GLObjects.Textures.TextureFormats;
using OpenTK.Graphics.OpenGL;
using System.Windows.Media.Imaging;

namespace G1Tool.Formats
{
    /// <summary>
    /// This class can only handle GT1G0600 textures.
    /// </summary>
    public class G1T
    {
        #region Properties
        public uint Version { get; private set; }
        public uint FileSize { get; private set; }
        public uint TextureCount {
            get { return (uint)Textures.Count; }
        }

        public uint UnkValue1 { get; private set; }
        public List<G1Texture> Textures { get; private set; }
        #endregion

        public G1T()
        {
            Textures = new List<G1Texture>();
        }

        public void Read(string filename)
        {
            Read(new EndianBinaryReader(filename, Endianness.Little));
        }

        public void Read(EndianBinaryReader r)
        {
            if (r.ReadUInt32() != 0x47315447)
            {
                MessageBox.Show("This file is not a valid Koei Tecmo G1T.", "Invalid file");
                return;
            }

            Version = r.ReadUInt32();

            if (Version != 0x30303630)
            {
                MessageBox.Show("This version of the G1T format is unsupported.", "Unsupported version");
                return;
            }

            FileSize = r.ReadUInt32();

            if (FileSize != r.Length)
            {
                MessageBox.Show("There is a mismatch between the reported file size and the actual one.", "File size mismatch");
                return;
            }

            uint offsetTableAddress = r.ReadUInt32();

            uint texCount = r.ReadUInt32();

            UnkValue1 = r.ReadUInt32(); //0x10 or 0xA, no idea what the difference is
            uint unk2 = r.ReadUInt32();

            uint[] normalMapFlags = r.ReadUInt32s((int)texCount);
            uint[] offsetTable = r.ReadUInt32s((int)texCount);

            for (int i = 0; i < texCount; i++)
            {
                G1Texture texture = new G1Texture();

                r.SeekBegin(offsetTableAddress + offsetTable[i]);

                texture.NormalMapFlags = normalMapFlags[i];
                texture.Read(r);
                Textures.Add(texture);
            }
        }

        public void Write(string filename)
        {
            Write(new EndianBinaryWriter(filename, Endianness.Little));
        }

        public void Write(EndianBinaryWriter w)
        {
            w.Write("GT1G", StringBinaryFormat.FixedLength, 4); // Magic
            w.Write("0600", StringBinaryFormat.FixedLength, 4); //Version
            long filesizepos = w.Position;
            w.WritePadding(8); // Skip filesize and table address for now
            w.Write((UInt32)TextureCount);
            w.Write((UInt32)UnkValue1);
            w.WritePadding(4);

            foreach (G1Texture texture in Textures)
                if (texture.NormalMapFlags == 3)
                    w.Write((UInt32)3);
                else
                    w.Write((UInt32)0);

            long offsettablepos = w.Position;
            w.WritePadding((int)(4 * TextureCount)); // Skip offset table for now

            List<UInt32> offsets = new List<uint>();

            foreach (G1Texture texture in Textures)
            {
                offsets.Add((uint)w.Position - (uint)offsettablepos);

                w.Write((byte)(texture.MipMapCount << 4));
                //w.Write((byte)(1 << 4)); //Until mipmap generation is added

                if (texture.pixelInternalFormat == PixelInternalFormat.CompressedRgbS3tcDxt1Ext)
                    w.Write((byte)0x59);
                else if (texture.pixelInternalFormat == PixelInternalFormat.CompressedRgbaS3tcDxt5Ext)
                    w.Write((byte)0x5B);
                else if (texture.pixelInternalFormat == PixelInternalFormat.Rgba8)
                    w.Write((byte)0x1);

                int dimensions = (int)Math.Log(texture.Width, 2) | (int)Math.Log(texture.Height, 2) << 4;
                w.Write((short)dimensions);

                if(texture.UsesExtraHeader)
                {
                    w.Write((uint)0x10211000);
                    w.Write((uint)0xC);
                    w.WritePadding(4);
                    w.Write((uint)0x1000000);
                }
                else
                {
                    w.Write((uint)0x211000);
                }

                texture.Mipmap.Bind();

                int level = 0;

                for(level = 0; level < texture.MipMapCount; level++)
                {
                    int imageSize;
                    if (TextureFormatTools.IsCompressed(texture.pixelInternalFormat))
                        GL.GetTexLevelParameter(TextureTarget.Texture2D, level, GetTextureParameter.TextureCompressedImageSize, out imageSize);
                    else
                        imageSize = texture.Mipmap.GetImageData(0).Length;

                    byte[] mipmap = new byte[imageSize];

                    if(TextureFormatTools.IsCompressed(texture.pixelInternalFormat))
                        GL.GetCompressedTexImage(TextureTarget.Texture2D, level, mipmap);
                    else
                    {
                        GL.GetTexImage(TextureTarget.Texture2D, level, texture.pixelFormat, texture.pixelType, mipmap);
                        //mipmap = texture.Mipmap.GetImageData(0);
                    }

                    w.Write(mipmap);
                }
            }

            w.SeekBegin(filesizepos);
            w.Write((uint)w.Length);
            w.Write((uint)offsettablepos);
            w.SeekBegin(offsettablepos);
            foreach (UInt32 offset in offsets)
                w.Write(offset);

            w.Close();
        }

        public void AddTexture(G1Texture texture)
        {
            Textures.Add(texture);
        }
    }

    public class G1Texture
    {
        public PixelInternalFormat pixelInternalFormat;
        public PixelFormat pixelFormat;
        public PixelType pixelType = PixelType.UnsignedByte;

        #region Properties
        public int Width { get; private set; }
        public int Height { get; private set; }
        /// <summary>
        /// Width and Height need to be set before using this.
        /// </summary>
        public int TextureSize
        {
            get
            {
                switch (pixelInternalFormat)
                {
                    case PixelInternalFormat.Rgba8:
                        return (Width * Height) * 4;
                    case PixelInternalFormat.CompressedRgbS3tcDxt1Ext:
                        return (Width * Height / 2);
                    case PixelInternalFormat.CompressedRgbaS3tcDxt5Ext:
                        return (Width * Height);
                    default:
                        return -1;
                }
            }
        }
        public byte MipMapCount { get; set; }
        public uint NormalMapFlags { get; set; }
        public bool UsesExtraHeader { get; set; }
        public InternalFormat InternalFormat { get; set; }
        public Texture2D Mipmap { get; set; }
        #endregion

        public G1Texture()
        {
            Mipmap = new Texture2D();
        }

        public void Read(EndianBinaryReader r)
        {
            MipMapCount = (byte)(r.ReadByte() >> 4);
            SetPixelFormatFromG1TextureCompressionType(r.ReadByte());

            short dimensions = r.ReadInt16();
            Width = (int)Math.Pow(2, (dimensions & 0xF));
            Height = (int)Math.Pow(2, (dimensions >> 4));

            // Skip the Extra Texture Header until we know what it does
            if ((r.ReadUInt32() >> 24) == 0x10)
            {
                UsesExtraHeader = true;
                r.SeekCurrent(0xC);
            }
            
            if(TextureFormatTools.IsCompressed(pixelInternalFormat))
            {
                Mipmap.LoadImageData(Width, Height, r.ReadBytes(TextureSize), (InternalFormat)pixelInternalFormat);
            }
            else
            {
                Mipmap.LoadImageData(Width, Height, r.ReadBytes(TextureSize), new TextureFormatUncompressed(pixelInternalFormat, pixelFormat, pixelType));
            }
            
            

        }

        public void Replace(DDS newTex)
        {
            Width = newTex.Width;
            Height = newTex.Height;
            MipMapCount = (byte)newTex.MipMapCount;
            pixelInternalFormat = (PixelInternalFormat)newTex.InternalFormat;
            Mipmap = newTex.Texture;
        }

        public void Replace(BitmapSource newTex)
        {
            Width = newTex.PixelWidth;
            Height = newTex.PixelHeight;
            MipMapCount = 1;
            byte[] image = new byte[Width * Height * 4];
            newTex.CopyPixels(image, Width * 4, 0);
            Mipmap.LoadImageData(Width, Height, image, new TextureFormatUncompressed(pixelInternalFormat, pixelFormat, pixelType));
        }

        public byte GetG1TextureCompressionType()
        {
            switch(pixelInternalFormat)
            {
                case PixelInternalFormat.Rgba8:
                    return 0x1;
                case PixelInternalFormat.CompressedRgbS3tcDxt1Ext:
                    return 0x59;
                case PixelInternalFormat.CompressedRgbaS3tcDxt5Ext:
                    return 0x5B;
                default:
                    throw new NotImplementedException("Not implemented yet gtfo");
            }
        }

        public void SetPixelFormatFromG1TextureCompressionType(byte comp)
        {
            switch(comp)
            {
                case 0x1:
                    pixelInternalFormat = PixelInternalFormat.Rgba8;
                    pixelFormat = PixelFormat.Bgra;
                    pixelType = PixelType.UnsignedByte;
                    break;
                case 0x59:
                    pixelInternalFormat = PixelInternalFormat.CompressedRgbS3tcDxt1Ext;
                    break;
                case 0x5b:
                    pixelInternalFormat = PixelInternalFormat.CompressedRgbaS3tcDxt5Ext;
                    break;
                default:
                    throw new NotImplementedException("Not implemented, biatch");
            }
        }

        public static PixelInternalFormat GetPixelInternalFormatForTextures(byte value)
        {
            switch (value)
            {
                case 0x1:
                    return PixelInternalFormat.Rgba8;
                case 0x6:
                case 0x59:
                    return PixelInternalFormat.CompressedRgbS3tcDxt1Ext;
                case 0x8:
                case 0x5B:
                    return PixelInternalFormat.CompressedRgbaS3tcDxt5Ext;
                default:
                    throw new NotImplementedException($"Unknown internal pixel format: 0x{value:X}");
            }
        }

        public override string ToString()
        {
            return base.ToString();
        }
    }
}
