using AVANebulus.Engine;

namespace AVANebulus.Tests;

public class TowerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public void Every_tower_can_be_climbed_from_its_foot_to_the_top(int index)
    {
        var t = TowerBook.Build(index);
        var reach = Graph.Build(t).Reach(new Spot(t.StartCol, t.Rows - 1));
        Assert.Contains(reach, s => s.Row == t.TopRow);
    }

    [Fact]
    public void A_ring_of_ledge_round_the_foot_and_the_top()
    {
        var t = TowerBook.Build(1);
        for (int c = 0; c < Field.Cols; c++)
        {
            Assert.Equal(Tile.Ledge, t.Grid[c, t.Rows - 1]);
            Assert.Equal(Tile.Ledge, t.Grid[c, t.TopRow]);
        }
    }

    [Fact]
    public void Doorways_come_in_pairs_on_opposite_sides()
    {
        int doors = 0;
        for (int i = 0; i < TowerBook.Count; i++)
        {
            var t = TowerBook.Build(i);
            for (int c = 0; c < Field.Cols; c++)
            for (int r = 0; r < t.Rows; r++)
                if (t.Grid[c, r] == Tile.Door)
                {
                    doors++;
                    Assert.Equal(Tile.Door, t.At(c + Field.Cols / 2, r));
                }
        }
        Assert.True(doors > 0);
    }

    [Fact]
    public void Each_tower_is_taller_with_more_on_it_and_more_time()
    {
        for (int i = 1; i < TowerBook.Count; i++)
        {
            var a = TowerBook.Build(i - 1);
            var b = TowerBook.Build(i);
            Assert.True(b.Rows > a.Rows);
            Assert.True(b.Balls.Count + b.Eyes.Count >= a.Balls.Count + a.Eyes.Count);
            Assert.True(b.Time > a.Time);
        }
    }

    [Fact]
    public void The_way_round_wraps()
    {
        Assert.Equal(0, TowerDef.Wrap(Field.Cols));
        Assert.Equal(Field.Cols - 1, TowerDef.Wrap(-1));
        Assert.Equal(-20, TowerDef.Delta(10, Field.Round - 10));
        Assert.Equal(20, TowerDef.Delta(Field.Round - 10, 10));
    }
}
