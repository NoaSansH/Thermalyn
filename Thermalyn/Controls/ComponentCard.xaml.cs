// SPDX-FileCopyrightText: 2026 Thermalyn Project
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Thermalyn.Controls;

public partial class ComponentCard : UserControl
{
    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(
        nameof(Heading), typeof(string), typeof(ComponentCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconDataProperty = DependencyProperty.Register(
        nameof(IconData), typeof(Geometry), typeof(ComponentCard), new PropertyMetadata(null));

    public string Heading
    {
        get => (string)GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    public Geometry IconData
    {
        get => (Geometry)GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public ComponentCard()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            var narrow = ActualWidth < 360;
            Grid.SetRow(HeaderState, narrow ? 1 : 0);
            HeaderState.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            HeaderState.Margin = narrow ? new Thickness(0, 7, 0, 0) : new Thickness(0);
            Grid.SetRow(SecondaryMetricsGrid, narrow ? 1 : 0);
            SecondaryMetricsGrid.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            SecondaryMetricsGrid.Margin = narrow ? new Thickness(0, 8, 0, 0) : new Thickness(0, 0, 0, 8);
        };
    }

    public TextBlock State => StateText;
    public TextBlock NameValue => ComponentName;
    public TextBlock Value => PrimaryValue;
    public TextBlock Unit => PrimaryUnit;
    public TextBlock SecondaryCaption => SecondaryLabel;
    public TextBlock Secondary => SecondaryValue;
    public StackPanel FanHost => FanMetric;
    public TextBlock Fan => FanValue;
    public HistoryChart History => Chart;
    public UniformGrid Summary => NoGraphSummary;
    public TextBlock SummaryTemperature => NoGraphTemperature;
    public TextBlock SummaryThirdLabel => ThirdLabel;
    public TextBlock SummaryThirdValue => ThirdValue;
    public TextBlock SummaryFourthLabel => FourthLabel;
    public TextBlock SummaryFourthValue => FourthValue;
}
