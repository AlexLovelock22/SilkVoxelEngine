using Silk.NET.OpenGL;
using SixLabors.ImageSharp.PixelFormats;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace VoxelEngine_Silk.Net_1._0.Helpers
{
    public static class TextureManager
    {
        public static unsafe uint LoadTextureAtlas(string path, GL gl)
        {
            uint handle = gl.GenTexture();
            gl.BindTexture(GLEnum.Texture2D, handle);

            using (var img = Image.Load<Rgba32>(path))
            {
                Console.WriteLine($"[Texture Load] Path: {path} | Size: {img.Width}x{img.Height}");
                img.Mutate(x => x.Flip(FlipMode.Vertical));
                var pixels = new byte[4 * img.Width * img.Height];
                img.CopyPixelDataTo(pixels);

                gl.PixelStore(GLEnum.UnpackAlignment, 1);
                fixed (void* p = pixels)
                {
                    gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba, (uint)img.Width, (uint)img.Height, 0, GLEnum.Rgba, GLEnum.UnsignedByte, p);
                }
            }

            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Nearest);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Nearest);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.ClampToEdge);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.ClampToEdge);

            return handle; // Send the ID back to the caller
        }


        public static unsafe uint LoadTexture(string path, GL gl)
        {
            uint handle = gl.GenTexture();
            gl.BindTexture(GLEnum.Texture2D, handle);

            using (var image = Image.Load<Rgba32>(path))
            {
                // OpenGL expects the first pixel to be bottom-left
                image.Mutate(x => x.Flip(FlipMode.Vertical));

                byte[] pixels = new byte[4 * image.Width * image.Height];
                image.CopyPixelDataTo(pixels);

                // This ensures OpenGL handles the byte alignment of your pixel data correctly
                gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

                fixed (byte* ptr = pixels)
                {
                    gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba, (uint)image.Width, (uint)image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
                }

                // Generate mipmaps so the shader has data at all "levels"
                gl.GenerateMipmap(GLEnum.Texture2D);
            }

            // Standard Pixel Art Settings
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.NearestMipmapLinear); // Use mipmaps but keep pixels sharp
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Nearest);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.Repeat);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.Repeat);

            return handle;
        }


        public static void Bind(GL gl, uint handle, uint unit = 0)
        {
            // unit 0 = Texture0, unit 1 = Texture1, etc.
            gl.ActiveTexture(GLEnum.Texture0 + (int)unit);
            gl.BindTexture(GLEnum.Texture2D, handle);
        }
    }
}
