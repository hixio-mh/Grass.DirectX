﻿using System;
using SharpDX;
using SharpDX.Toolkit.Input;
using Wheat.Components;
using Wheat.Core;

namespace Wheat.Grass
{
    // Use these namespaces here to override SharpDX.Direct3D11
    using SharpDX.Toolkit;
    using SharpDX.Toolkit.Graphics;

    /// <summary>
    /// Main class for creating and rendering the grass
    /// </summary>
    class GrassController
    {
        #region Fields

        private readonly Texture2D texture;
        private readonly Texture2D alphaTexture;
        private Effect effect;
        private Buffer<VertexPositionNormalTexture> vertexBuffer;
        private VertexInputLayout vertexInputLayout;
        private VertexPositionNormalTexture[] vertices;

        private Texture2D terrainHeightMap;
        private float[,] heightData;

        private BoundingFrustum boundingFrustum;
        private readonly GameCore core;

        private const float TerranSize = 256;

        #endregion

        #region Properties

        /// <summary>
        /// The number of rows for x and y of the grid
        /// </summary>
        public int NumberOfPatchRows { get; private set; }

        /// <summary>
        /// Returns the number of roots.
        /// </summary>
        public int NumberOfRoots { get; private set; }

        /// <summary>
        /// Returns the number of patches.
        /// </summary>
        public int NumberOfPatches { get; private set; }

        /// <summary>
        /// Returns the number of roots in in one patch.
        /// </summary>
        public int NumberOfRootsInPatch { get; private set; }

        /// <summary>
        /// Returns the number of rows in in one patch.
        /// </summary>
        public int NumberOfRowsInPatch { get; private set; }

        #endregion

        #region Public Methods

        /// <summary>
        /// Initializes a new instance of the <see cref="GrassController"/> class.
        /// </summary>
        /// <param name="core">The core.</param>
        public GrassController(GameCore core)
        {
            this.core = core;
            this.effect = this.core.ContentManager.Load<Effect>("Effects/Grass");
            this.texture = this.core.ContentManager.Load<Texture2D>("Textures/abstractGrass");
            this.alphaTexture = this.core.ContentManager.Load<Texture2D>("Textures/grassAlphaLOD1");
            this.terrainHeightMap = this.core.ContentManager.Load<Texture2D>("Textures/heightMap");
            this.LoadHeightData();
            this.GenerateField();
        }
        
        /// <summary>
        /// 
        /// </summary>
        public void Update()
        {
            if (this.core.KeyboardState.IsKeyPressed(Keys.F5))
            {
                this.effect = this.core.ContentManager.Load<Effect>("Effects/Grass");
            }

            this.boundingFrustum = new BoundingFrustum(this.core.Camera.View * this.core.Camera.Projection);
        }

        /// <summary>
        /// Draws grass field.
        /// </summary>
        /// <param name="gameTime">The game time.</param>
        /// <param name="camera">The camera.</param>
        public void Draw(GameTime gameTime, Camera camera)
        {
            this.effect.Parameters["World"].SetValue(Matrix.Identity);
            this.effect.Parameters["View"].SetValue(camera.View);
            this.effect.Parameters["Projection"].SetValue(camera.Projection);
            if (this.effect.Parameters["Texture"] != null) this.effect.Parameters["Texture"].SetResource(this.texture);
            if (this.effect.Parameters["AlphaTexture"] != null) this.effect.Parameters["AlphaTexture"].SetResource(this.alphaTexture);
            this.effect.Parameters["Time"].SetValue(new Vector2((float)gameTime.TotalGameTime.TotalMilliseconds / 1000, gameTime.ElapsedGameTime.Milliseconds));
            this.effect.Parameters["LightPosition"].SetValue(this.core.ShadowCamera.Position);
            this.effect.Parameters["CameraPosition"].SetValue(this.core.Camera.Position);

            this.core.GraphicsDevice.SetVertexBuffer(this.vertexBuffer);
            this.core.GraphicsDevice.SetVertexInputLayout(this.vertexInputLayout);

            // Draw patches
            int startRoot = 0;
            
            for (int i = 0; i < this.NumberOfPatches; i++)
            {
                BoundingSphere boundingSphere = new BoundingSphere(this.vertices[startRoot + this.NumberOfRootsInPatch / 2].Position, 5.0f);

                if (!this.boundingFrustum.Intersects(ref boundingSphere))
                {
                    startRoot += this.NumberOfRootsInPatch;
                }
                else
                {
                    Vector3 cameraPosition = this.core.Camera.Position;
                    cameraPosition.Y = 0;
                    Vector3 difference = cameraPosition - this.vertices[startRoot].Position;
                    float distance = difference.Length();
                    int rootsToDraw = this.NumberOfRootsInPatch - (int) (distance*0.4f);

                    if (distance > (int)LevelOfDetail.Level4)
                    {
                        this.effect.Techniques["LevelOfDetail4"].Passes[0].Apply();
                    }
                    else if (distance > (int)LevelOfDetail.Level3)
                    {
                        this.effect.Techniques["LevelOfDetail3"].Passes[0].Apply();
                    }
                    else if (distance > (int)LevelOfDetail.Level2)
                    {
                        this.effect.Techniques["LevelOfDetail2"].Passes[0].Apply();
                    }
                    else
                    {
                        this.effect.Techniques["LevelOfDetail1"].Passes[0].Apply();
                        rootsToDraw = this.NumberOfRootsInPatch;
                    }

                    if (rootsToDraw < 20)
                    {
                        rootsToDraw = 20;
                    }

                    this.core.GraphicsDevice.Draw(PrimitiveType.PointList, rootsToDraw, startRoot);
                    startRoot += this.NumberOfRootsInPatch;
                }
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Generates the complete grass field.
        /// </summary>
        private void GenerateField(int numberOfPatchRows = 50, int numberOfRootsInPatch = 150, int numberOfRowsInPatch = 10)
        {
            this.NumberOfPatchRows = numberOfPatchRows;
            this.NumberOfRootsInPatch = numberOfRootsInPatch;
            this.NumberOfRowsInPatch = numberOfRowsInPatch;

            this.NumberOfPatches = this.NumberOfPatchRows * this.NumberOfPatchRows;
            this.NumberOfRoots = this.NumberOfPatches * this.NumberOfRootsInPatch;

            this.vertices = new VertexPositionNormalTexture[this.NumberOfRoots];

            Random rnd = new Random();
            int currentVertex = 0;

            Vector3 startPosition = new Vector3(0, 0, 0);
            Vector3 patchSize = new Vector3(TerranSize / this.NumberOfPatchRows, 0, TerranSize / this.NumberOfPatchRows);

            // Generate grid of patches
            for (int x = 0; x < this.NumberOfPatchRows; x++)
            {
                for (int y = 0; y < this.NumberOfPatchRows; y++)
                {
                    currentVertex = this.GeneratePatch(startPosition, patchSize, currentVertex, rnd);
                    startPosition.X += patchSize.X;
                }

                startPosition.X = 0;
                startPosition.Z += patchSize.Z;
            }

            this.vertexBuffer = Buffer.Vertex.New(this.core.GraphicsDevice, this.vertices);
            this.vertexInputLayout = VertexInputLayout.FromBuffer(0, this.vertexBuffer);
        }

        /// <summary>
        /// Generates all the roots for one patch.
        /// </summary>
        /// <param name="startPosition">Start of the roots in object space</param>
        /// <param name="patchSize">Size of the patch</param>
        /// <param name="currentVertex">Index of the vertex</param>
        /// <param name="rnd">Random seed</param>
        /// <returns></returns>
        private int GeneratePatch(Vector3 startPosition, Vector3 patchSize, int currentVertex, Random rnd)
        {
            for (var i = 0; i < this.NumberOfRootsInPatch; i++)
            {
                // Generate random numbers within the patch size
                var randomizedZDistance = (float) rnd.NextDouble(0, patchSize.Z);
                var randomizedXDistance = (float) rnd.NextDouble(0, patchSize.X);
   
                int indexX = (int)((startPosition.X + randomizedXDistance));
                int indexZ = (int)((startPosition.Z + randomizedZDistance));

                var currentPosition = new Vector3(startPosition.X + (randomizedXDistance), heightData[indexX, indexZ], startPosition.Z + randomizedZDistance);
                this.vertices[currentVertex] = new VertexPositionNormalTexture(currentPosition, Vector3.Up, new Vector2(0, 0));
                currentVertex++;
            }

            return currentVertex;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="heightMap"></param>
        private void LoadHeightData()
        {
            int width = this.terrainHeightMap.Width;
            int height = this.terrainHeightMap.Height;

            Image image = this.terrainHeightMap.GetDataAsImage();
            heightData = new float[width, height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    PixelData.R8G8B8A8 pixel = image.PixelBuffer[0].GetPixel<PixelData.R8G8B8A8>(x, y);
                    heightData[x, y] = (pixel.R - 225f)/5f;
                }
            }
        }

        #endregion
    }
}
