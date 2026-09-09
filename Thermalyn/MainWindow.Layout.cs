using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Thermalyn;

public partial class MainWindow
{
    private void ApplyResponsiveLayout()
    {
        if (!IsInitialized) return;
        var narrow = ActualWidth < 560;
        var contentWidth = Math.Max(280, ActualWidth);
        var shortWindow = ActualHeight < 720;
        if (DetailsView.Visibility == Visibility.Visible) ConfigureDetailsPage();
        else if (SettingsView.Visibility == Visibility.Visible) ConfigurePanel(SettingsView, 640);
        var navigationAllowed = _panelOverlayMode || DetailsView.Visibility != Visibility.Visible && SettingsView.Visibility != Visibility.Visible;
        TopNavigationRow.Height = !narrow && navigationAllowed ? new GridLength(47) : new GridLength(0);
        NavigationBar.Visibility = !narrow && navigationAllowed ? Visibility.Visible : Visibility.Collapsed;
        BottomNavigationRow.Height = narrow && navigationAllowed ? new GridLength(56) : new GridLength(0);
        BottomNavigation.Visibility = narrow && navigationAllowed ? Visibility.Visible : Visibility.Collapsed;
        var pagePadding = contentWidth < 480 ? 14 : contentWidth < 900 ? 22 : 32;
        NavigationBar.Padding = new Thickness(pagePadding, 0, pagePadding, 0);
        HardwareDriverBanner.Padding = new Thickness(pagePadding, 12, pagePadding, 12);

        HeroGrid.Columns = contentWidth < 680 ? 1 : 2;
        CompactCardsGrid.Columns = contentWidth < 680 ? 1 : 2;
        DetailedChartGrid.Columns = contentWidth < 820 ? 1 : 2;
        var detailedCardWidth = contentWidth / DetailedChartGrid.Columns;
        DetailedCpuMetrics.Columns = DetailedGpuMetrics.Columns = detailedCardWidth >= 560 ? 4 : detailedCardWidth >= 330 ? 2 : 1;
        SpaceMetricRows(DetailedCpuMetrics); SpaceMetricRows(DetailedGpuMetrics);
        DetailedHardwareGrid.Columns = contentWidth >= 820 ? 2 : 1;
        DetailedSensorGrid.Columns = contentWidth >= 820 ? 2 : 1;
        ProcessGrid.Columns = contentWidth >= 1080 ? 3 : contentWidth >= 720 && !shortWindow ? 2 : 1;
        DetailedRamBarHost.Visibility = contentWidth >= 560 ? Visibility.Visible : Visibility.Collapsed;
        var detailWide = DetailsView.Visibility == Visibility.Visible && contentWidth >= 980;
        DetailOverviewPrimaryColumn.Width = new GridLength(detailWide ? 1.7 : 1, GridUnitType.Star);
        DetailOverviewSecondaryColumn.Width = detailWide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(DetailChartCard, 0); Grid.SetRow(DetailChartCard, 0);
        Grid.SetColumn(DetailSummaryGrid, detailWide ? 1 : 0); Grid.SetRow(DetailSummaryGrid, detailWide ? 0 : 1);
        DetailSummaryGrid.Columns = detailWide ? 1 : contentWidth >= 820 ? _settings.ShowFans ? 5 : 4 : contentWidth >= 520 ? 2 : 1;
        DetailSummaryGrid.Margin = detailWide ? new Thickness(24, 0, 0, 0) : new Thickness(0, 14, 0, 0);

        var pageMargin = new Thickness(pagePadding, shortWindow ? 14 : pagePadding - 4, pagePadding, shortWindow ? 14 : pagePadding - 4);
        BalancedView.Padding = MiniView.Padding = CompactView.Padding = DetailedView.Padding = pageMargin;
        DetailsView.Padding = SettingsView.Padding = pageMargin;

        var heroChart = shortWindow ? 88d : 120d;
        CpuHeroCard.History.Height = GpuHeroCard.History.Height = heroChart + 30;
        var detailedChart = shortWindow ? 96d : 120d;
        DetailedCpuHistory.Height = DetailedGpuHistory.Height = detailedChart + 26;
        var ramChart = shortWindow ? 100d : 130d;
        DetailedRamHistory.Height = ramChart + 26;
        var detailChart = contentWidth < 520 ? 130d : shortWindow ? 150d : detailWide ? 220d : 180d;
        DetailChartHistory.Height = detailChart + 26;

        var heroSize = contentWidth < 480 ? 44d : shortWindow ? 50d : contentWidth < 900 ? 52d : 56d;
        CpuHeroCard.Value.FontSize = GpuHeroCard.Value.FontSize = heroSize;
        CpuHeroCard.Value.LineHeight = GpuHeroCard.Value.LineHeight = heroSize * 1.1;
        var denseSize = contentWidth < 480 ? 34d : shortWindow ? 38d : 42d;
        CompactCpuTemp.FontSize = CompactGpuTemp.FontSize = DetailedCpuValue.FontSize = DetailedGpuValue.FontSize = DetailedRamPercent.FontSize = denseSize;
        CompactCpuTemp.LineHeight = CompactGpuTemp.LineHeight = DetailedCpuValue.LineHeight = DetailedGpuValue.LineHeight = DetailedRamPercent.LineHeight = denseSize * 1.1;
        CompactCardsGrid.Columns = contentWidth < 560 ? 1 : 2;
        MiniCardsGrid.Columns = contentWidth < 680 ? 1 : 3;
        SpaceCards(MiniCardsGrid);
        var miniNarrow = contentWidth < 520;
        Grid.SetRow(MiniActions, miniNarrow ? 1 : 0);
        MiniActions.HorizontalAlignment = miniNarrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        MiniActions.Margin = miniNarrow ? new Thickness(0, 10, 0, 0) : new Thickness(0);
        SpaceCards(HeroGrid); SpaceCards(CompactCardsGrid); SpaceCards(DetailedChartGrid);
        var compactNarrow = (contentWidth - pagePadding * 2) / CompactCardsGrid.Columns < 360;
        var detailedNarrow = (contentWidth - pagePadding * 2) / DetailedChartGrid.Columns < 360;
        ReflowBadge(CompactCpuStateBadge, compactNarrow); ReflowBadge(CompactGpuStateBadge, compactNarrow);
        ReflowBadge(DetailedCpuStateBadge, detailedNarrow); ReflowBadge(DetailedGpuStateBadge, detailedNarrow);
        ReflowBadge(DetailedCpuRangeHost, detailedNarrow); ReflowBadge(DetailedGpuRangeHost, detailedNarrow);
        StatusText.Visibility = CompactStatusText.Visibility = DetailedStatusText.Visibility = ActiveGpuNote.Visibility = shortWindow ? Visibility.Collapsed : Visibility.Visible;

        SystemSectionTitle.Margin = new Thickness(0, shortWindow ? 12 : 24, 0, shortWindow ? 8 : 12);

        SecondaryMetrics.Columns = contentWidth >= 900 ? 4 : contentWidth >= 520 ? 2 : 1;
        SpaceMetricRows(SecondaryMetrics);

        var ramWrap = contentWidth < 680;
        var labelWidth = contentWidth < 900 ? 180d : 250d;
        RamLabelColumn.Width = ramWrap ? new GridLength(1, GridUnitType.Star) : new GridLength(labelWidth);
        CompactRamLabelColumn.Width = FanLabelColumn.Width = ramWrap ? new GridLength(1, GridUnitType.Star) : new GridLength(labelWidth);
        RamValueColumn.Width = ramWrap ? new GridLength(58) : new GridLength(78);
        RamNarrowRow.Height = ramWrap ? GridLength.Auto : new GridLength(0);
        Grid.SetColumnSpan(RamSystemLabel, ramWrap ? 3 : 1);
        Grid.SetRow(RamBarHost, ramWrap ? 1 : 0); Grid.SetColumn(RamBarHost, ramWrap ? 0 : 1); Grid.SetColumnSpan(RamBarHost, ramWrap ? 2 : 1);
        Grid.SetRow(RamPercentValue, ramWrap ? 1 : 0); Grid.SetColumn(RamPercentValue, 2);
        RamBarHost.Width = ramWrap ? double.NaN : 220;
        RamBarHost.HorizontalAlignment = ramWrap ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        RamBarHost.Margin = ramWrap ? new Thickness(0, 13, 16, 0) : new Thickness(18, 0, 18, 0);
    }

    private static void SpaceCards(UniformGrid grid)
    {
        foreach (var child in grid.Children.OfType<FrameworkElement>())
            child.Margin = new Thickness(8, 0, 8, grid.Columns == 1 ? 14 : 0);
    }

    private static void ReflowBadge(FrameworkElement badge, bool narrow)
    {
        Grid.SetRow(badge, narrow ? 1 : 0);
        badge.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        badge.Margin = narrow ? new Thickness(0, 7, 0, 0) : new Thickness(0);
    }

    private static void SpaceMetricRows(UniformGrid grid)
    {
        var wrapped = grid.Columns < grid.Children.Count;
        foreach (var child in grid.Children.OfType<FrameworkElement>()) child.Margin = new Thickness(0, 0, 16, wrapped ? 12 : 0);
    }

    private static void ShowIfMeasured(UIElement host, TextBlock value, bool allowed = true) =>
        host.Visibility = allowed && value.Text is not ("—" or "") ? Visibility.Visible : Visibility.Collapsed;
}
