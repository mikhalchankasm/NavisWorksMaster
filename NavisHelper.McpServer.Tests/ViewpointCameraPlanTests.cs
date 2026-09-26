using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ViewpointCameraPlanTests
{
    public static IEnumerable<object[]> ValidDirections()
    {
        for (var index = 1; index <= 64; index++)
        {
            yield return new object[]
            {
                (double)index,
                (double)((index % 7) + 1),
                (double)((index % 5) + 1),
            };
        }
    }

    public static IEnumerable<object[]> ValidTargets()
    {
        for (var index = 1; index <= 32; index++)
        {
            yield return new object[]
            {
                1000d + index,
                -500d + index * 2,
                25d - index * 0.25,
            };
        }
    }

    [Theory]
    [MemberData(nameof(ValidDirections))]
    public void Build_Direction_PreservesExactVectorAndDerivesTarget(double x, double y, double z)
    {
        var request = ValidRequest();
        request.Target = null;
        request.Direction = Point(x, y, z);

        var plan = ViewpointCameraPlanHelper.Build(request);

        AssertPoint(plan.Direction, x, y, z);
        AssertPoint(plan.Target, 10 + x, 20 + y, 30 + z);
        Assert.Equal(ViewpointCameraPlanHelper.Perspective, plan.Projection);
    }

    [Theory]
    [MemberData(nameof(ValidTargets))]
    public void Build_Target_PreservesExactTargetAndDerivesDirection(double x, double y, double z)
    {
        var request = ValidRequest();
        request.Target = Point(x, y, z);

        var plan = ViewpointCameraPlanHelper.Build(request);

        AssertPoint(plan.Target, x, y, z);
        AssertPoint(plan.Direction, x - 10, y - 20, z - 30);
    }

    [Theory]
    [InlineData("perspective", "perspective")]
    [InlineData(" PERSPECTIVE ", "perspective")]
    [InlineData("orthographic", "orthographic")]
    [InlineData(" ORTHOGRAPHIC ", "orthographic")]
    [InlineData("ortho", "orthographic")]
    [InlineData(" ORTHO ", "orthographic")]
    public void Build_NormalizesSupportedProjectionAliases(string input, string expected)
    {
        var request = ValidRequest();
        request.Projection = input;

        Assert.Equal(expected, ViewpointCameraPlanHelper.Build(request).Projection);
    }

    [Theory]
    [InlineData(0.000001)]
    [InlineData(0.1)]
    [InlineData(0.7853981633974483)]
    [InlineData(1.5707963267948966)]
    [InlineData(3.141592653589792)]
    public void Build_AcceptsFinitePerspectiveHeightFieldBelowPi(double value)
    {
        var request = ValidRequest();
        request.HeightField = value;

        Assert.Equal(value, ViewpointCameraPlanHelper.Build(request).HeightField);
    }

    [Theory]
    [InlineData(0.000001)]
    [InlineData(1)]
    [InlineData(250)]
    [InlineData(1000000)]
    public void Build_AcceptsPositiveOrthographicHeightFieldInDocumentUnits(double value)
    {
        var request = ValidRequest();
        request.Projection = "ortho";
        request.HeightField = value;

        Assert.Equal(value, ViewpointCameraPlanHelper.Build(request).HeightField);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(3.141592653589793)]
    [InlineData(4)]
    public void Build_RejectsInvalidPerspectiveHeightField(double value)
    {
        var request = ValidRequest();
        request.HeightField = value;

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Fact]
    public void Build_RequiresExactlyOneTargetOrDirection()
    {
        var request = ValidRequest();
        request.Direction = Point(1, 0, 0);
        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));

        request.Target = null;
        request.Direction = null;
        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Theory]
    [InlineData(10, 20, 30)]
    [InlineData(10.0000000000001, 20, 30)]
    public void Build_RejectsZeroOrNumericallyDegenerateLookVector(double x, double y, double z)
    {
        var request = ValidRequest();
        request.Target = Point(x, y, z);

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Theory]
    [InlineData(1, 0, 0, 2, 0, 0)]
    [InlineData(1, 2, 3, -2, -4, -6)]
    [InlineData(0, 0, 1, 0, 0, 10)]
    [InlineData(0, 3, 0, 0, -7, 0)]
    public void Build_RejectsParallelUpAndDirection(double dx, double dy, double dz, double ux, double uy, double uz)
    {
        var request = ValidRequest();
        request.Target = null;
        request.Direction = Point(dx, dy, dz);
        request.Up = Point(ux, uy, uz);

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Fact]
    public void Build_RejectsZeroUp()
    {
        var request = ValidRequest();
        request.Up = Point(0, 0, 0);

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Theory]
    [InlineData("current")]
    [InlineData("parallel")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Build_RejectsUnsupportedProjection(string value)
    {
        var request = ValidRequest();
        request.Projection = value;

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Theory]
    [InlineData(double.NaN, 1, 2)]
    [InlineData(1, double.NaN, 2)]
    [InlineData(1, 2, double.NaN)]
    [InlineData(double.PositiveInfinity, 1, 2)]
    [InlineData(1, double.NegativeInfinity, 2)]
    public void Build_RejectsNonFinitePosition(double x, double y, double z)
    {
        var request = ValidRequest();
        request.Position = Point(x, y, z);

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Theory]
    [InlineData(double.NaN, 1, 2)]
    [InlineData(1, double.PositiveInfinity, 2)]
    [InlineData(1, 2, double.NegativeInfinity)]
    public void Build_RejectsNonFiniteTarget(double x, double y, double z)
    {
        var request = ValidRequest();
        request.Target = Point(x, y, z);

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Theory]
    [InlineData(double.NaN, 1, 2)]
    [InlineData(1, double.PositiveInfinity, 2)]
    [InlineData(1, 2, double.NegativeInfinity)]
    public void Build_RejectsNonFiniteUp(double x, double y, double z)
    {
        var request = ValidRequest();
        request.Up = Point(x, y, z);

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Fact]
    public void Build_RejectsDirectionWhoseDerivedTargetOverflows()
    {
        var request = ValidRequest();
        request.Position = Point(double.MaxValue, 0, 0);
        request.Target = null;
        request.Direction = Point(double.MaxValue, 1, 0);

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Fact]
    public void Build_RejectsDirectionThatVanishesAgainstThePosition()
    {
        var request = ValidRequest();
        request.Position = Point(9007199254740992, 0, 0);
        request.Target = null;
        request.Direction = Point(1, 0, 0);

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Fact]
    public void Build_AcceptsGeoreferencedDirectionWithinPreservationTolerance()
    {
        var request = ValidRequest();
        request.Position = Point(500000000, 7000000000, 100000);
        request.Target = null;
        request.Direction = Point(1, 1, -0.5);

        var plan = ViewpointCameraPlanHelper.Build(request);

        AssertPoint(plan.Direction, 1, 1, -0.5);
        Assert.Equal(1.5, plan.FocalDistance, 9);
    }

    [Fact]
    public void Build_RejectsDerivedTargetThatLosesDirectionComponent()
    {
        var request = ValidRequest();
        request.Position = Point(9007199254740992, 0, 0);
        request.Target = null;
        request.Direction = Point(1, 1, 0);

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Fact]
    public void Build_RejectsTargetWhoseDerivedDirectionOverflows()
    {
        var request = ValidRequest();
        request.Position = Point(-double.MaxValue, 0, 0);
        request.Target = Point(double.MaxValue, 1, 0);

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(1)]
    [InlineData(25.5)]
    [InlineData(1000)]
    public void Build_PointZoomCreatesExactCube(double halfSize)
    {
        var request = ValidRequest();
        request.Projection = "ortho";
        request.Target = Point(100, 200, 300);
        request.ZoomTo = new ViewpointCameraZoomTo
        {
            Point = Point(100, 200, 300),
            PointHalfSize = halfSize,
        };

        var box = ViewpointCameraPlanHelper.Build(request).ZoomBox;

        AssertPoint(box.Min, 100 - halfSize, 200 - halfSize, 300 - halfSize);
        AssertPoint(box.Max, 100 + halfSize, 200 + halfSize, 300 + halfSize);
        AssertPoint(box.Center, 100, 200, 300);
        AssertPoint(box.Size, halfSize * 2, halfSize * 2, halfSize * 2);
    }

    [Fact]
    public void Build_BoundingBoxZoomRecomputesDerivedFields()
    {
        var request = ValidRequest();
        request.Projection = "orthographic";
        request.Target = Point(5, 20, 130);
        request.ZoomTo = new ViewpointCameraZoomTo
        {
            BoundingBox = new BoundingBoxInfo
            {
                Min = Point(-5, 10, 100),
                Max = Point(15, 30, 160),
                Center = Point(999, 999, 999),
                Size = Point(999, 999, 999),
            },
        };

        var box = ViewpointCameraPlanHelper.Build(request).ZoomBox;

        AssertPoint(box.Center, 5, 20, 130);
        AssertPoint(box.Size, 20, 20, 60);
    }

    [Fact]
    public void Build_RejectsPerspectiveZoomTo()
    {
        var request = ValidRequest();
        request.ZoomTo = new ViewpointCameraZoomTo
        {
            Point = Point(0, 0, 0),
            PointHalfSize = 1,
        };

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Fact]
    public void Build_RejectsOffCenterZoomPoint()
    {
        var request = ValidRequest();
        request.Projection = "ortho";
        request.ZoomTo = new ViewpointCameraZoomTo
        {
            Point = Point(11, 22, 34),
            PointHalfSize = 1,
        };

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Fact]
    public void Build_RejectsOffCenterZoomPointMaskedByLargeCoordinate()
    {
        var request = ValidRequest();
        request.Projection = "ortho";
        request.Target = Point(9007199254740992, 0, 0);
        request.ZoomTo = new ViewpointCameraZoomTo
        {
            Point = Point(9007199254740992, 1000000, 0),
            PointHalfSize = 2,
        };

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Fact]
    public void PointsNearlyEqual_ComparesEachAxisAgainstItsOwnScale()
    {
        Assert.False(ViewpointCameraPlanHelper.PointsNearlyEqual(
            Point(9007199254740992, 0, 0),
            Point(9007199254740992, 1000000, 0)));
        Assert.True(ViewpointCameraPlanHelper.PointsNearlyEqual(
            Point(9007199254740992, 0, 0),
            Point(9007199254740992, 0.0000000001, 0)));
    }

    [Fact]
    public void Build_RejectsOffCenterZoomBox()
    {
        var request = ValidRequest();
        request.Projection = "ortho";
        request.ZoomTo = new ViewpointCameraZoomTo
        {
            BoundingBox = new BoundingBoxInfo
            {
                Min = Point(0, 0, 0),
                Max = Point(2, 2, 2),
            },
        };

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Fact]
    public void Build_RejectsPointHalfSizeWithBoundingBox()
    {
        var request = ValidRequest();
        request.Projection = "ortho";
        request.Target = Point(0.5, 0.5, 0.5);
        request.ZoomTo = new ViewpointCameraZoomTo
        {
            PointHalfSize = 1,
            BoundingBox = ValidBox(),
        };

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Fact]
    public void Build_RejectsNumericallyUnstableNearlyParallelUp()
    {
        var request = ValidRequest();
        request.Target = null;
        request.Direction = Point(1, 0, 0);
        request.Up = Point(1, 0.0000001, 0);

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Fact]
    public void Build_RequiresExactlyOneZoomShape()
    {
        var request = ValidRequest();
        request.Projection = "ortho";
        request.ZoomTo = new ViewpointCameraZoomTo();
        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));

        request.ZoomTo.Point = Point(0, 0, 0);
        request.ZoomTo.PointHalfSize = 1;
        request.ZoomTo.BoundingBox = ValidBox();
        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Build_RejectsInvalidPointHalfSize(double? value)
    {
        var request = ValidRequest();
        request.Projection = "ortho";
        request.ZoomTo = new ViewpointCameraZoomTo
        {
            Point = Point(0, 0, 0),
            PointHalfSize = value,
        };

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Fact]
    public void Build_RejectsPointZoomWhoseCubeOverflows()
    {
        var request = ValidRequest();
        request.Projection = "ortho";
        request.Target = Point(double.MaxValue, 0, 0);
        request.ZoomTo = new ViewpointCameraZoomTo
        {
            Point = request.Target,
            PointHalfSize = double.MaxValue,
        };

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Fact]
    public void Build_RejectsPointZoomBoxCollapsedByRounding()
    {
        var request = ValidRequest();
        request.Projection = "ortho";
        request.Target = Point(9007199254740992, 0, 0);
        request.ZoomTo = new ViewpointCameraZoomTo
        {
            Point = Point(9007199254740992, 0, 0),
            PointHalfSize = 0.1,
        };

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Fact]
    public void Build_RejectsPointZoomBoxWithOneSidedRoundingCollapse()
    {
        var request = ValidRequest();
        request.Projection = "ortho";
        request.Target = Point(9007199254740992, 0, 0);
        request.ZoomTo = new ViewpointCameraZoomTo
        {
            Point = Point(9007199254740992, 0, 0),
            PointHalfSize = 1,
        };

        var exception = Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
        Assert.Contains("zoomTo.point and pointHalfSize must produce a box", exception.Message);
    }

    [Fact]
    public void Build_RejectsBoundingBoxWhoseExtentOverflows()
    {
        var request = ValidRequest();
        request.Projection = "ortho";
        request.Target = Point(0, 0, 0);
        request.ZoomTo = new ViewpointCameraZoomTo
        {
            BoundingBox = new BoundingBoxInfo
            {
                Min = Point(-double.MaxValue, -1, -1),
                Max = Point(double.MaxValue, 1, 1),
            },
        };

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(-1, 0, 0)]
    public void Build_RejectsNonIncreasingBoxAxis(double dx, double dy, double dz)
    {
        var request = ValidRequest();
        request.Projection = "ortho";
        request.ZoomTo = new ViewpointCameraZoomTo
        {
            BoundingBox = new BoundingBoxInfo
            {
                Min = Point(0, 0, 0),
                Max = Point(dx, dy, dz),
            },
        };

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Theory]
    [InlineData(" Camera 01 ", " Folder/Sub ", "Camera 01", "Folder/Sub")]
    [InlineData("A", "", "A", "")]
    [InlineData("  Русская метка  ", "  Метки/День  ", "Русская метка", "Метки/День")]
    public void Build_NormalizesOptionalSaveFields(string name, string folder, string expectedName, string expectedFolder)
    {
        var request = ValidRequest();
        request.SaveName = name;
        request.SaveFolderPath = folder;

        var plan = ViewpointCameraPlanHelper.Build(request);

        Assert.True(plan.SaveRequested);
        Assert.Equal(expectedName, plan.SaveName);
        Assert.Equal(expectedFolder, plan.SaveFolderPath);
    }

    [Fact]
    public void Build_RejectsFolderWithoutSaveName()
    {
        var request = ValidRequest();
        request.SaveFolderPath = "Markers";

        Assert.Throws<ArgumentException>(() => ViewpointCameraPlanHelper.Build(request));
    }

    [Fact]
    public void Build_ReturnsIndependentCoordinateCopies()
    {
        var request = ValidRequest();
        var plan = ViewpointCameraPlanHelper.Build(request);

        request.Position.X = 999;
        request.Target.Y = 999;
        request.Up.Z = 999;

        AssertPoint(plan.Position, 10, 20, 30);
        AssertPoint(plan.Target, 11, 22, 33);
        AssertPoint(plan.Up, 0, 0, 1);
    }

    private static ViewpointSetCameraRequest ValidRequest()
    {
        return new ViewpointSetCameraRequest
        {
            Position = Point(10, 20, 30),
            Target = Point(11, 22, 33),
            Up = Point(0, 0, 1),
            Projection = "perspective",
        };
    }

    private static BoundingBoxInfo ValidBox()
    {
        return new BoundingBoxInfo { Min = Point(0, 0, 0), Max = Point(1, 1, 1) };
    }

    private static Point3Info Point(double x, double y, double z)
    {
        return new Point3Info { X = x, Y = y, Z = z };
    }

    private static void AssertPoint(Point3Info point, double x, double y, double z)
    {
        Assert.NotNull(point);
        Assert.Equal(x, point.X, 12);
        Assert.Equal(y, point.Y, 12);
        Assert.Equal(z, point.Z, 12);
    }
}
