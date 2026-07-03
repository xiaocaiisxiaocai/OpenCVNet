using System;
using VisionInspection.Core.Models;
using Xunit;

namespace VisionInspection.Tests
{
    public class RecipeValidatorTests
    {
        private static Recipe ValidRecipe()
        {
            return new Recipe
            {
                ModelCode = "1",
                Stations =
                {
                    new Station { Index = 0, Roi = new RoiRect(0, 0, 10, 10), Threshold = 0.5 },
                    new Station { Index = 1, Roi = new RoiRect(10, 0, 10, 10), Threshold = 0.5 }
                }
            };
        }

        [Fact]
        public void Validate_Accepts_Valid_Recipe()
        {
            RecipeValidator.Validate(ValidRecipe(), bitmapWordCount: 1);
        }

        [Fact]
        public void Validate_Rejects_Non_Numeric_Model_Code()
        {
            var recipe = ValidRecipe();
            recipe.ModelCode = "A1";

            Assert.Throws<ArgumentException>(() => RecipeValidator.Validate(recipe, bitmapWordCount: 1));
        }

        [Fact]
        public void Validate_Rejects_Duplicate_Station_Index()
        {
            var recipe = ValidRecipe();
            recipe.Stations[1].Index = 0;

            Assert.Throws<ArgumentException>(() => RecipeValidator.Validate(recipe, bitmapWordCount: 1));
        }

        [Fact]
        public void Validate_Rejects_Station_Index_Outside_Bitmap()
        {
            var recipe = ValidRecipe();
            recipe.Stations[1].Index = 16;

            Assert.Throws<ArgumentException>(() => RecipeValidator.Validate(recipe, bitmapWordCount: 1));
        }

        [Fact]
        public void Validate_Rejects_Invalid_Threshold_And_Empty_Roi()
        {
            var recipe = ValidRecipe();
            recipe.Stations[0].Threshold = 1.5;
            Assert.Throws<ArgumentException>(() => RecipeValidator.Validate(recipe, bitmapWordCount: 1));

            recipe = ValidRecipe();
            recipe.Stations[0].Roi = new RoiRect(0, 0, 0, 10);
            Assert.Throws<ArgumentException>(() => RecipeValidator.Validate(recipe, bitmapWordCount: 1));
        }

        [Fact]
        public void Validate_Rejects_Invalid_Fiducial_Quality_Gates()
        {
            var recipe = ValidRecipe();
            recipe.Fiducial.MaxRotationDegrees = -1;
            Assert.Throws<ArgumentException>(() => RecipeValidator.Validate(recipe, bitmapWordCount: 1));

            recipe = ValidRecipe();
            recipe.Fiducial.MinScale = 1.2;
            recipe.Fiducial.MaxScale = 1.1;
            Assert.Throws<ArgumentException>(() => RecipeValidator.Validate(recipe, bitmapWordCount: 1));
        }
    }
}
