using System.Collections.Generic;
namespace Zenith.Mac;
internal static class BuiltinLayouts
{
 public static readonly Dictionary<string,string> Xml = new()
 {
["classic"] = """
<UserControl
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:local="clr-namespace:ClassicRender"
             xmlns:ui="clr-namespace:ZenithEngine.UI;assembly=ZenithEngine"
             xmlns:bme="clr-namespace:ZenithEngine;assembly=ZenithEngine"
             xmlns:xctk="http://schemas.xceed.com/wpf/xaml/toolkit" xmlns:System="clr-namespace:System;assembly=mscorlib" x:Class="ClassicRender.SettingsCtrl"
             mc:Ignorable="d" Height="366.234" Width="753.096">
    <UserControl.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary>
                    <ResourceDictionary.MergedDictionaries>
                        <ResourceDictionary Source="pack://application:,,,/ZenithEngine;component/UI/Material.xaml"/>
                        <ResourceDictionary Source="pack://siteoforigin:,,,/Languages/en/classic.xaml" />
                    </ResourceDictionary.MergedDictionaries>
                </ResourceDictionary>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </UserControl.Resources>
    <DockPanel>
        <StackPanel Margin="10">
            <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,0,0,0">
                <Label Content="{DynamicResource firstNote}" HorizontalAlignment="Left" VerticalAlignment="Top" DockPanel.Dock="Left"/>
                <ui:NumberSelect x:Name="firstNote" Value="1" Maximum="254" Minimum="0" Margin="5,0,0,2" HorizontalAlignment="Left" Width="80" ValueChanged="Nud_ValueChanged"  />
                <Label Content="{DynamicResource lastNote}" HorizontalAlignment="Left" Margin="10,0,0,0" VerticalAlignment="Top"/>
                <ui:NumberSelect x:Name="lastNote" Value="1" Maximum="255" Minimum="1" Margin="5,0,0,2" HorizontalAlignment="Left" Width="80" ValueChanged="Nud_ValueChanged"  />
            </DockPanel>
            <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,10,0,0" VerticalAlignment="Top">
                <Label Content="{DynamicResource pianoHeight}" HorizontalAlignment="Left" Margin="0" VerticalAlignment="Top"/>
                <ui:NumberSelect x:Name="pianoHeight" Value="1" Maximum="100" Minimum="1" Margin="6,0,0,2" HorizontalAlignment="Left" Width="80" ValueChanged="Nud_ValueChanged"  />
            </DockPanel>
            <ui:BetterCheckbox x:Name="sameWidth" Text="{DynamicResource sameWidthNotes}" HorizontalAlignment="Left" Margin="0,10,0,0" VerticalAlignment="Top" IsChecked="True" CheckToggled="SameWidth_Checked"/>
            <ui:BetterCheckbox x:Name="blackNotesAbove" Text="{DynamicResource blackNotesAbove}" HorizontalAlignment="Left" Margin="0,10,0,0" VerticalAlignment="Top" CheckToggled="BlackNotesAbove_Checked"/>
            <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,10,0,0" VerticalAlignment="Top" Width="528" >
                <Label Content="{DynamicResource noteScreenTime}" HorizontalAlignment="Left" Margin="0,0,0,0" VerticalAlignment="Top"/>
                <ui:ValueSlider x:Name="noteDeltaScreenTime" Maximum="12" DecimalPoints="2" Minimum="2" TrueMin="1" TrueMax="100000" ValueChanged="NoteDeltaScreenTime_ValueChanged" Width="305" VerticalAlignment="Top"/>
            </DockPanel>
        </StackPanel>
        <bme:NoteColorPalettePick x:FieldModifier="public" x:Name="paletteList" Margin="0,10,10,10" HorizontalAlignment="Right" Width="184"/>
    </DockPanel>
</UserControl>

""",
["flat"] = """
<UserControl
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:ui="clr-namespace:ZenithEngine.UI;assembly=ZenithEngine"
             xmlns:local="clr-namespace:FlatRender"
             xmlns:xctk="http://schemas.xceed.com/wpf/xaml/toolkit"
    xmlns:bme="clr-namespace:ZenithEngine;assembly=ZenithEngine" x:Class="FlatRender.SettingsCtrl"
             mc:Ignorable="d" Height="328.9" Width="651.763">
    <UserControl.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary>
                    <ResourceDictionary.MergedDictionaries>
                        <ResourceDictionary Source="pack://application:,,,/ZenithEngine;component/UI/Material.xaml"/>
                        <ResourceDictionary Source="pack://siteoforigin:,,,/Languages/en/flat.xaml" />
                    </ResourceDictionary.MergedDictionaries>
                </ResourceDictionary>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </UserControl.Resources>
    <DockPanel>
        <StackPanel Margin="10">
            <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,0,0,0">
                <Label Content="{DynamicResource firstNote}" HorizontalAlignment="Left" VerticalAlignment="Top" DockPanel.Dock="Left"/>
                <ui:NumberSelect x:Name="firstNote" Value="1" Maximum="254" Minimum="0" Margin="5,0,0,2" HorizontalAlignment="Left" Width="80" ValueChanged="Nud_ValueChanged"  />
                <Label Content="{DynamicResource lastNote}" HorizontalAlignment="Left" Margin="10,0,0,0" VerticalAlignment="Top"/>
                <ui:NumberSelect x:Name="lastNote" Value="1" Maximum="255" Minimum="1" Margin="5,0,0,2" HorizontalAlignment="Left" Width="80" ValueChanged="Nud_ValueChanged"  />
            </DockPanel>
            <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,10,0,0" VerticalAlignment="Top">
                <Label Content="{DynamicResource noteScreenTime}" HorizontalAlignment="Left" Margin="0,0,0,0" VerticalAlignment="Top"/>
                <ui:ValueSlider x:Name="noteDeltaScreenTime" Maximum="12" DecimalPoints="2" Minimum="2" TrueMin="1" TrueMax="100000" ValueChanged="NoteDeltaScreenTime_ValueChanged" Width="305" VerticalAlignment="Top"/>
            </DockPanel>
            <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,10,0,0" VerticalAlignment="Top">
                <Label Content="{DynamicResource pianoHeight}" HorizontalAlignment="Left" Margin="0" VerticalAlignment="Top"/>
                <ui:NumberSelect x:Name="pianoHeight" Value="1" Maximum="100" Minimum="1" Margin="6,0,0,2" HorizontalAlignment="Left" Width="80" ValueChanged="Nud_ValueChanged"  />
            </DockPanel>
            <ui:BetterCheckbox x:Name="sameWidth" Text="{DynamicResource sameWidthNotes}" HorizontalAlignment="Left" Margin="0,10,0,0" VerticalAlignment="Top" IsChecked="True" CheckToggled="SameWidth_Checked"/>
        </StackPanel>
        <bme:NoteColorPalettePick x:FieldModifier="public" x:Name="paletteList" Margin="0,10,10,10" HorizontalAlignment="Right" Width="184"/>
    </DockPanel>
</UserControl>

""",
["pfa"] = """
<UserControl
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:ui="clr-namespace:ZenithEngine.UI;assembly=ZenithEngine"
             xmlns:local="clr-namespace:PFARender"
             xmlns:xctk="http://schemas.xceed.com/wpf/xaml/toolkit"
             xmlns:bme="clr-namespace:ZenithEngine;assembly=ZenithEngine" x:Class="PFARender.SettingsCtrl"
             mc:Ignorable="d"
             d:DesignHeight="450" d:DesignWidth="800">
    <UserControl.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary>
                    <ResourceDictionary.MergedDictionaries>
                        <ResourceDictionary Source="pack://siteoforigin:,,,/Languages/en/pfa.xaml" />
                    </ResourceDictionary.MergedDictionaries>
                </ResourceDictionary>
                <ResourceDictionary Source="pack://application:,,,/ZenithEngine;component/UI/Material.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </UserControl.Resources>
    <DockPanel>
        <StackPanel Margin="10">
            <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,0,0,0">
                <Label Content="{DynamicResource firstNote}" HorizontalAlignment="Left" VerticalAlignment="Top" DockPanel.Dock="Left"/>
                <ui:NumberSelect x:Name="firstNote" Value="1" Maximum="254" Minimum="0" Margin="5,0,0,2" HorizontalAlignment="Left" Width="80" ValueChanged="Nud_ValueChanged"  />
                <Label Content="{DynamicResource lastNote}" HorizontalAlignment="Left" Margin="10,0,0,0" VerticalAlignment="Top"/>
                <ui:NumberSelect x:Name="lastNote" Value="1" Maximum="255" Minimum="1" Margin="5,0,0,2" HorizontalAlignment="Left" Width="80" ValueChanged="Nud_ValueChanged"  />
            </DockPanel>
            <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,10,0,0" VerticalAlignment="Top">
                <Label Content="{DynamicResource noteScreenTime}" HorizontalAlignment="Left" Margin="0,0,0,0" VerticalAlignment="Top"/>
                <ui:ValueSlider x:Name="noteDeltaScreenTime" Maximum="12" DecimalPoints="2" Minimum="2" TrueMin="1" TrueMax="100000" ValueChanged="NoteDeltaScreenTime_ValueChanged" Width="305" VerticalAlignment="Top"/>
            </DockPanel>
            <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,10,0,0" VerticalAlignment="Top">
                <Label Content="{DynamicResource pianoHeight}" HorizontalAlignment="Left" Margin="0" VerticalAlignment="Top"/>
                <ui:NumberSelect x:Name="pianoHeight" Value="1" Maximum="100" Minimum="1" Margin="6,0,0,2" HorizontalAlignment="Left" Width="80" ValueChanged="Nud_ValueChanged"  />
            </DockPanel>
            <ui:BetterCheckbox x:Name="sameWidth" Text="{DynamicResource sameWidthNotes}" HorizontalAlignment="Left" Margin="0,10,0,0" VerticalAlignment="Top" IsChecked="True" CheckToggled="SameWidth_Checked"/>
            <ui:BetterCheckbox Name="middleCSquare" Text="{DynamicResource middleCSquare}" HorizontalAlignment="Left" Margin="0,10,0,0" VerticalAlignment="Top" CheckToggled="MiddleCSquare_Checked"/>
            <ui:BetterCheckbox x:Name="blackNotesAbove" Text="{DynamicResource blackNotesAbove}" HorizontalAlignment="Left" Margin="0,10,0,0" VerticalAlignment="Top" CheckToggled="BlackNotesAbove_Checked"/>
            <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,10,0,0" VerticalAlignment="Top">
                <Label x:Name="topCol" Content="{DynamicResource topColor}" HorizontalAlignment="Left" VerticalAlignment="Top" DockPanel.Dock="Left"/>
                <TextBlock HorizontalAlignment="Left" Margin="0,6,2,0" TextWrapping="Wrap" Text="#" VerticalAlignment="Top" FontSize="14"/>
                <TextBox x:Name="barColorHex" HorizontalAlignment="Left" FontSize="14" MaxLength="6" Height="24" Margin="0,2,0,0" TextWrapping="Wrap" Text="950A06" VerticalAlignment="Top" Width="58" TextChanged="BarColorHex_TextChanged"/>
            </DockPanel>
            <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,10,0,0" VerticalAlignment="Top" >
                <Label Content="{DynamicResource borderWidth}" DockPanel.Dock="Left" VerticalAlignment="Top"/>
                <ui:NumberSelect x:Name="borderWidth" Value="1" DecimalPoints="2" Step="0.1" Maximum="2" Minimum="0" Margin="5,0,0,0" HorizontalAlignment="Left" Width="80" Height="26" VerticalAlignment="Top" ValueChanged="Nud_ValueChanged"  />
            </DockPanel>
        </StackPanel>
        <bme:NoteColorPalettePick x:FieldModifier="public" x:Name="paletteList" Margin="0,10,10,10" HorizontalAlignment="Right" Width="184"/>
    </DockPanel>
</UserControl>

""",
["textured"] = """
<UserControl
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:ui="clr-namespace:ZenithEngine.UI;assembly=ZenithEngine"
             xmlns:local="clr-namespace:TexturedRender"
             xmlns:bme="clr-namespace:ZenithEngine;assembly=ZenithEngine"
             xmlns:xctk="http://schemas.xceed.com/wpf/xaml/toolkit" x:Class="TexturedRender.SettingsCtrl"
             mc:Ignorable="d"
             d:DesignHeight="450" d:DesignWidth="800">
    <UserControl.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary>
                    <ResourceDictionary.MergedDictionaries>
                        <ResourceDictionary Source="pack://siteoforigin:,,,/Languages/en/textured.xaml" />
                    </ResourceDictionary.MergedDictionaries>
                </ResourceDictionary>
                <ResourceDictionary Source="pack://application:,,,/ZenithEngine;component/UI/Material.xaml"/>
            </ResourceDictionary.MergedDictionaries>
            <Style TargetType="TabControl" BasedOn="{StaticResource SubTabs}"/>
            <Style TargetType="TabItem" BasedOn="{StaticResource SubTabItems}"/>
        </ResourceDictionary>
    </UserControl.Resources>
    <Grid>
        <TabControl Margin="10">
            <TabItem Header="{DynamicResource resourcesTab}">
                <Grid Margin="10">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="204*"/>
                        <ColumnDefinition Width="371*"/>
                        <ColumnDefinition Width="199*"/>
                    </Grid.ColumnDefinitions>
                    <bme:NoteColorPalettePick x:FieldModifier="public" x:Name="paletteList" Margin="0" Grid.Column="2" Grid.RowSpan="2" />
                    <DockPanel LastChildFill="True" Grid.RowSpan="2">
                        <Button x:Name="reloadListButton" Content="{DynamicResource reloadList}" Margin="0,0,0,0" Height="26" DockPanel.Dock="Top" Click="ReloadButton_Click"/>
                        <Button x:Name="reloadPackButton" Content="{DynamicResource reloadPack}" Margin="0,10,0,0" Height="26" DockPanel.Dock="Top" Click="ReloadPackButton_Click"/>
                        <Button x:Name="openFolderButton" Content="{DynamicResource openFolder}" Margin="0,10,0,0" Height="26" DockPanel.Dock="Bottom" Click="openFolderButton_Click"/>
                        <ListBox x:Name="pluginList" Margin="0,10,0,0" SelectionChanged="PluginList_SelectionChanged"/>
                    </DockPanel>
                    <DockPanel Grid.Column="1" LastChildFill="True">
                    <TextBox MinHeight="100" DockPanel.Dock="Bottom" TextAlignment="Center" x:Name="pluginDesc" Grid.Column="1" Margin="10,10,10,0" TextWrapping="Wrap" IsEnabled="False" Grid.Row="1"/>
                    <Image x:Name="previewImg" Grid.Column="1" Margin="10,0,10,0"/>
                    </DockPanel>
                </Grid>
            </TabItem>
            <TabItem Header="{DynamicResource switchesTab}" Name="switchTab">
                <Grid>
                    <StackPanel Name="switchPanel" Margin="10">
                    </StackPanel>
                </Grid>
            </TabItem>
            <TabItem Header="{DynamicResource miscTab}">
                <StackPanel Margin="10">
                    <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,0,0,0" VerticalAlignment="Top">
                        <Label Content="{DynamicResource firstNote}" HorizontalAlignment="Left" VerticalAlignment="Top" DockPanel.Dock="Left"/>
                        <ui:NumberSelect x:Name="firstNote" Value="0" Maximum="254" Minimum="0" Margin="5,0,0,0" HorizontalAlignment="Left" Width="80" Height="26" VerticalAlignment="Top" ValueChanged="Nud_ValueChanged"  />
                        <Label Content="{DynamicResource lastNote}" HorizontalAlignment="Left" Margin="10,0,0,0" VerticalAlignment="Top"/>
                        <ui:NumberSelect x:Name="lastNote" Value="127" Maximum="255" Minimum="1" Margin="5,0,0,0" HorizontalAlignment="Left" Width="80" Height="26" VerticalAlignment="Top" ValueChanged="Nud_ValueChanged"  />
                    </DockPanel>
                    <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,10,0,0" VerticalAlignment="Top" Width="528" >
                        <Label Content="{DynamicResource noteScreenTime}" HorizontalAlignment="Left" Margin="0,0,0,0" VerticalAlignment="Top"/>
                        <ui:ValueSlider x:Name="noteDeltaScreenTime" Maximum="12" DecimalPoints="2" Minimum="2" TrueMin="1" TrueMax="100000" ValueChanged="NoteDeltaScreenTime_ValueChanged" Width="305" VerticalAlignment="Top"/>
                    </DockPanel>
                    <ui:BetterCheckbox x:Name="blackNotesAbove" Text="{DynamicResource blackNotesAbove}" HorizontalAlignment="Left" Margin="0,10,0,0" VerticalAlignment="Top" CheckToggled="BlackNotesAbove_Checked" />
                </StackPanel>
            </TabItem>
        </TabControl>
    </Grid>
</UserControl>

""",
["notecounter"] = """
<UserControl x:Class="NoteCountRender.SettingsCtrl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="clr-namespace:ZenithEngine.UI;assembly=ZenithEngine"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:local="clr-namespace:NoteCountRender"
             xmlns:xctk="http://schemas.xceed.com/wpf/xaml/toolkit"
             mc:Ignorable="d"
             d:DesignHeight="450" d:DesignWidth="800">
    <UserControl.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary>
                    <ResourceDictionary.MergedDictionaries>
                        <ResourceDictionary Source="pack://siteoforigin:,,,/Languages/en/notecounter.xaml" />
                        <ResourceDictionary Source="pack://application:,,,/ZenithEngine;component/UI/Material.xaml"/>
                    </ResourceDictionary.MergedDictionaries>
                </ResourceDictionary>
            </ResourceDictionary.MergedDictionaries>
            <Style TargetType="TabControl" BasedOn="{StaticResource SubTabs}"/>
            <Style TargetType="TabItem" BasedOn="{StaticResource SubTabItems}"/>
        </ResourceDictionary>
    </UserControl.Resources>
    <Grid>
        <TabControl Margin="10,10,10,10">
            <TabItem Header="{DynamicResource Render}">
                <DockPanel Margin="10">
                    <DockPanel DockPanel.Dock="Top">
                        <StackPanel Width="530">
                            <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,0,0,0" VerticalAlignment="Top">
                                <Label DockPanel.Dock="Left" Content="{DynamicResource fontSize}" VerticalAlignment="Top"/>
                                <ui:NumberSelect x:Name="fontSize" Value="1"  Maximum="15360" Minimum="1" HorizontalAlignment="Left" Width="99" IsEnabled="{Binding IsEnabled, ElementName=notPreviewingOrRendering}"  DockPanel.Dock="Left" Margin="5,0,0,0" ValueChanged="FontSize_ValueChanged"  />
                                <Label DockPanel.Dock="Left" Content="{DynamicResource font}" VerticalAlignment="Top"/>
                                <ComboBox x:Name="fontSelect" DockPanel.Dock="Left" Width="120" Margin="5,0,0,0" SelectionChanged="FontSelect_SelectionChanged"/>
                                <ComboBox x:Name="fontStyle" HorizontalAlignment="Left" Margin="10,0,0,0" Width="120" SelectionChanged="FontStyle_SelectionChanged"/>
                            </DockPanel>
                            <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,10,0,0" VerticalAlignment="Top" >
                                <Label Content="{DynamicResource alignments}" HorizontalAlignment="Left" VerticalAlignment="Top" DockPanel.Dock="Left"/>
                                <ComboBox x:Name="alignSelect" DockPanel.Dock="Left" HorizontalAlignment="Left" Margin="5,0,0,0" VerticalAlignment="Top" Height="26" SelectedIndex="0" SelectionChanged="AlignSelect_SelectionChanged">
                                    <ComboBoxItem Content="{DynamicResource alTopLeft}"/>
                                    <ComboBoxItem Content="{DynamicResource alTopRight}"/>
                                    <ComboBoxItem Content="{DynamicResource alBottomLeft}"/>
                                    <ComboBoxItem Content="{DynamicResource alBottomRight}"/>
                                    <ComboBoxItem Content="{DynamicResource alTopSpread}"/>
                                    <ComboBoxItem Content="{DynamicResource alBottomSpread}"/>
                                </ComboBox>
                            </DockPanel>
                            <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,10,0,0" VerticalAlignment="Top">
                                <ComboBox x:Name="templates" DockPanel.Dock="Left" Width="120" SelectionChanged="Templates_SelectionChanged"/>
                                <Button x:Name="reload" Content="{DynamicResource reload}" HorizontalAlignment="Left" Padding="10,0,10,0" DockPanel.Dock="Left" Margin="5,0,0,0" Click="Reload_Click"/>
                                <Button x:Name="openFolder" Content="{DynamicResource openFolder}" HorizontalAlignment="Left" Padding="10,0,10,0" DockPanel.Dock="Left" Margin="5,0,0,0" Click="openFolder_Click"/>
                                <Button x:Name="newProfile" Content="{DynamicResource saveNew}" HorizontalAlignment="Left" Margin="15,0,0,0" Padding="10,0,10,0" Click="NewProfile_Click" DockPanel.Dock="Left"/>
                                <TextBox DockPanel.Dock="Left" x:Name="profileName" HorizontalAlignment="Left" Margin="5,0,0,0" TextWrapping="Wrap" Text="" Width="110"/>
                                <Label DockPanel.Dock="Right" HorizontalAlignment="Right" Content=".txt" />
                            </DockPanel>
                        </StackPanel>
                        <StackPanel Margin="30,0,0,0">
                            <Label Content="{DynamicResource thousandsSeparator}"/>
                            <ui:BetterRadio x:Name="useCommas" IsChecked="True" Text="{DynamicResource commas}" Margin="0,0,0,5" RadioChecked="useCommas_RadioChecked"/>
                            <!--<ui:BetterRadio x:Name="useDots" Text="Dots" Margin="0,0,0,5" RadioChecked="useCommas_RadioChecked"/>-->
                            <ui:BetterRadio x:Name="useNothing" Text="{DynamicResource nothing}" Margin="0,0,0,5" RadioChecked="useCommas_RadioChecked"/>
                            <ui:BetterCheckbox Margin="-4,2,0,0" x:Name="ZeroPadding" Text="{DynamicResource ZeroPadding}" CheckToggled="ZP" />
                        </StackPanel>
                    </DockPanel>
                    <TextBox AcceptsReturn="True" VerticalContentAlignment="Top" x:Name="textTemplate" Margin="0,10,0,0" TextWrapping="Wrap" Text="TextBox" TextChanged="TextTemplate_TextChanged"/>
                </DockPanel>
            </TabItem>
            <TabItem Header="{DynamicResource SaveCSV}">
                <StackPanel Margin="10">
                    <ui:BetterCheckbox Name="saveCsv" CheckToggled="saveCsv_Checked" Text="{DynamicResource saveToFile}" Margin="0,0,0,0"/>
                    <DockPanel Height="26" LastChildFill="True" Margin="0,10,0,0">
                        <Label HorizontalAlignment="Left" Margin="0,0,0,0" Content="{DynamicResource savePath}"/>
                        <Button x:Name="browseOutputSaveButton" Content="{DynamicResource browse}" HorizontalAlignment="Left" Margin="10,0,0,0" Padding="20,0,20,0" Click="browseOutputSaveButton_Click"/>
                        <TextBox x:Name="csvPath" Margin="10,0,0,0" IsEnabled="False" TextWrapping="Wrap" Text=""/>
                    </DockPanel>
                    <DockPanel Height="26" LastChildFill="True" Margin="0,10,0,0">
                        <Label HorizontalAlignment="Left" Margin="0,0,0,0" Content="{DynamicResource formatString}"/>
                        <TextBox x:Name="csvFormat" Margin="10,0,0,0" TextWrapping="Wrap" Text="" TextChanged="csvFormat_TextChanged"/>
                    </DockPanel>
                </StackPanel>
            </TabItem>
            <TabItem Header="{DynamicResource Padding}">
                <StackPanel Margin="10">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="2*" />
                            <ColumnDefinition Width="2*" />
                            <ColumnDefinition Width="3*" />
                            <ColumnDefinition Width="2*" />
                            <ColumnDefinition Width="9*" />
                        </Grid.ColumnDefinitions>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="30" />
                            <RowDefinition Height="30" />
                            <RowDefinition Height="30" />
                            <RowDefinition Height="30" />
                            <RowDefinition Height="30" />
                            <RowDefinition Height="30" />
                            <RowDefinition Height="30" />
                            <RowDefinition Height="15" />
                            <RowDefinition Height="30" />
                        </Grid.RowDefinitions>
                        <Label HorizontalAlignment="Right" Margin="0,1,0,0" Content="{DynamicResource BPMint}" />
                        <ui:NumberSelect Grid.Column="1" Grid.Row="0" x:Name="BPMint" Value="3" Maximum="5" Minimum="1" Width="64" IsEnabled="{Binding IsEnabled, ElementName=notPreviewingOrRendering}"  HorizontalAlignment="Left" Margin="10,-2,0,0" ValueChanged="Paddings_ValueChanged" Height="32" VerticalAlignment="Top"/>
                        <Label Grid.Column="2" Grid.Row="0" HorizontalAlignment="Right" Margin="10,1,0,0" Content="{DynamicResource BPMfrac}"/>
                        <ui:NumberSelect Grid.Column="3" Grid.Row="0" x:Name="BPMDecPt" Value="2" Maximum="12" Minimum="0" Width="64" IsEnabled="{Binding IsEnabled, ElementName=notPreviewingOrRendering}"  HorizontalAlignment="Left" Margin="10,-2,0,0" ValueChanged="Paddings_ValueChanged"/>
                        <Label Grid.Column="0" Grid.Row="1" HorizontalAlignment="Right" Margin="0,0,0,0" Content="{DynamicResource NoteCount}" />
                        <ui:NumberSelect Grid.Column="1" Grid.Row="1" x:Name="NoteCount" Value="5" Maximum="12" Minimum="1" Width="64" IsEnabled="{Binding IsEnabled, ElementName=notPreviewingOrRendering}"  HorizontalAlignment="Left" Margin="10,-2,0,0" ValueChanged="Paddings_ValueChanged"/>
                        <Label Grid.Column="0" Grid.Row="2" HorizontalAlignment="Right" Margin="0,0,0,0" Content="{DynamicResource Polyphony}" />
                        <ui:NumberSelect Grid.Column="1" Grid.Row="2" x:Name="Polyphony" Value="3" Maximum="9" Minimum="1" Width="64" IsEnabled="{Binding IsEnabled, ElementName=notPreviewingOrRendering}"  HorizontalAlignment="Left" Margin="10,-2,0,0" ValueChanged="Paddings_ValueChanged"/>
                        <Label Grid.Column="0" Grid.Row="3" HorizontalAlignment="Right" Margin="0,0,0,0" Content="{DynamicResource NPS}" />
                        <ui:NumberSelect Grid.Column="1" Grid.Row="3" x:Name="NPS" Value="3" Maximum="9" Minimum="1" Width="64" IsEnabled="{Binding IsEnabled, ElementName=notPreviewingOrRendering}"  HorizontalAlignment="Left" Margin="10,-2,0,0" ValueChanged="Paddings_ValueChanged"/>
                        <Label Grid.Column="0" Grid.Row="4" HorizontalAlignment="Right" Margin="0,0,0,0" Content="{DynamicResource Ticks}" />
                        <ui:NumberSelect Grid.Column="1" Grid.Row="4" x:Name="Ticks" Value="5" Maximum="9" Minimum="1" Width="64" IsEnabled="{Binding IsEnabled, ElementName=notPreviewingOrRendering}"  HorizontalAlignment="Left" Margin="10,-2,0,0" ValueChanged="Paddings_ValueChanged"/>
                        <Label Grid.Column="0" Grid.Row="5" HorizontalAlignment="Right" Margin="0,0,0,0" Content="{DynamicResource Bars}" />
                        <ui:NumberSelect Grid.Column="1" Grid.Row="5" x:Name="Bars" Value="3" Maximum="5" Minimum="1" Width="64" IsEnabled="{Binding IsEnabled, ElementName=notPreviewingOrRendering}"  HorizontalAlignment="Left" Margin="10,-2,0,0" ValueChanged="Paddings_ValueChanged"/>
                        <Label Grid.Column="0" Grid.Row="6" HorizontalAlignment="Right" Margin="0,0,0,0" Content="{DynamicResource Frames}" />
                        <ui:NumberSelect Grid.Column="1" Grid.Row="6" x:Name="Frames" Value="5" Maximum="5" Minimum="1" Width="64" IsEnabled="{Binding IsEnabled, ElementName=notPreviewingOrRendering}"  HorizontalAlignment="Left" Margin="10,-2,0,0" ValueChanged="Paddings_ValueChanged"/>
                        <Button Grid.ColumnSpan="2" Grid.Row="8" HorizontalAlignment="Center" x:Name="SetDefault" Content="{DynamicResource SetDefault}" Height ="25" Padding="20,2,20,2" Click="SetDefault_Click" />
                    </Grid>
                </StackPanel>
            </TabItem>
        </TabControl>

    </Grid>
</UserControl>

""",
["miditrail"] = """
<UserControl
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:local="clr-namespace:MIDITrailRender"
             xmlns:ui="clr-namespace:ZenithEngine.UI;assembly=ZenithEngine"
             xmlns:xctk="http://schemas.xceed.com/wpf/xaml/toolkit"
             xmlns:bme="clr-namespace:ZenithEngine;assembly=ZenithEngine" x:Class="MIDITrailRender.SettingsCtrl"
             mc:Ignorable="d" Height="500" Width="688.373">
    <UserControl.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary>
                    <ResourceDictionary.MergedDictionaries>
                        <ResourceDictionary Source="pack://siteoforigin:,,,/Languages/en/miditrail.xaml" />
                    </ResourceDictionary.MergedDictionaries>
                </ResourceDictionary>
                <ResourceDictionary Source="pack://application:,,,/ZenithEngine;component/UI/Material.xaml"/>
            </ResourceDictionary.MergedDictionaries>
            <Style TargetType="TabControl" BasedOn="{StaticResource SubTabs}"/>
            <Style TargetType="TabItem" BasedOn="{StaticResource SubTabItems}"/>
        </ResourceDictionary>
    </UserControl.Resources>
    <DockPanel LastChildFill="True" Margin="10">
        <DockPanel DockPanel.Dock="Bottom" Height="26" LastChildFill="False" Margin="0,0,0,0">
            <Button x:Name="deleteProfile" Content="{DynamicResource delete}" HorizontalAlignment="Left" Margin="0" Padding="20,0,20,0" Click="DeleteProfile_Click" DockPanel.Dock="Left"/>
            <ComboBox DropDownOpened="profileSelect_DropDownOpened" DockPanel.Dock="Left" x:Name="profileSelect" HorizontalAlignment="Left" Margin="5,0,0,0" Width="110" VerticalAlignment="Bottom" SelectionChanged="ProfileSelect_SelectionChanged"/>
            <Button x:Name="newProfile" Content="{DynamicResource saveNew}" HorizontalAlignment="Left" Margin="5,0,0,0" Padding="20,0,20,0" Click="NewProfile_Click" DockPanel.Dock="Left"/>
            <TextBox DockPanel.Dock="Left" x:Name="profileName" HorizontalAlignment="Left" Margin="5,0,0,0" TextWrapping="Wrap" Text="" Width="120"/>
            <Button x:Name="defaultsButton" Content="{DynamicResource loadDefault}" Margin="0" Click="DefaultsButton_Click" HorizontalAlignment="Right" Padding="20,0,20,0" DockPanel.Dock="Right"/>
        </DockPanel>
        <TabControl Margin="0,0,0,0">
            <TabItem Header="{DynamicResource visualsTab}">
                <DockPanel>
                    <StackPanel Margin="10">
                        <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,0,0,0" VerticalAlignment="Top">
                            <Label Content="{DynamicResource firstNote}" HorizontalAlignment="Left" VerticalAlignment="Top" DockPanel.Dock="Left"/>
                            <ui:NumberSelect x:Name="firstNote" Value="1" Maximum="254" Minimum="0" Margin="5,0,0,2" HorizontalAlignment="Left" Width="80" ValueChanged="Nud_ValueChanged"  />
                            <Label Content="{DynamicResource lastNote}" HorizontalAlignment="Left" Margin="10,0,0,0" VerticalAlignment="Top"/>
                            <ui:NumberSelect x:Name="lastNote" Value="1" Maximum="255" Minimum="1" Margin="5,0,0,2" HorizontalAlignment="Left" Width="80" ValueChanged="Nud_ValueChanged"  />
                        </DockPanel>
                        <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,10,0,0" VerticalAlignment="Top">
                            <Label Content="{DynamicResource keyDownSpeed}" HorizontalAlignment="Left" Margin="0" VerticalAlignment="Top"/>
                            <ui:NumberSelect x:Name="noteDownSpeed" DecimalPoints="2" Step="0.1" Value="0.2" Maximum="1" Minimum="0" Margin="5,0,0,2" HorizontalAlignment="Left" Width="65" Height="26" VerticalAlignment="Top" ValueChanged="Nud_ValueChanged"  />
                            <Label Content="{DynamicResource keyUpSpeed}" HorizontalAlignment="Left" Margin="10,0,0,0" VerticalAlignment="Top"/>
                            <ui:NumberSelect x:Name="noteUpSpeed" DecimalPoints="2" Step="0.1" Value="0.1" Maximum="1" Minimum="0" Margin="5,0,0,2" HorizontalAlignment="Left" Width="65" Height="26" VerticalAlignment="Top" ValueChanged="Nud_ValueChanged"  />
                        </DockPanel>
                        <ui:BetterCheckbox x:Name="tiltKeys" Text ="{DynamicResource tiltKeys}" HorizontalAlignment="Left" Margin="0,10,0,0" VerticalAlignment="Top"  CheckToggled="CheckboxChecked"/>
                        <ui:BetterCheckbox x:Name="boxNotes" Text="{DynamicResource 3dNotes}" HorizontalAlignment="Left" Margin="0,10,0,0" VerticalAlignment="Top" CheckToggled="BoxNotes_Checked"/>
                        <ui:BetterCheckbox IsEnabled="{Binding IsChecked, ElementName=boxNotes }" x:Name="lightShade" Text="{DynamicResource lightShade}" HorizontalAlignment="Left" Margin="0,10,0,0" VerticalAlignment="Top" CheckToggled="CheckboxChecked"/>
                        <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,10,0,0" VerticalAlignment="Top" >
                            <Label Content="{DynamicResource noteSpeed}" HorizontalAlignment="Left" VerticalAlignment="Top" DockPanel.Dock="Left"/>
                            <ui:ValueSlider x:Name="noteDeltaScreenTime" DecimalPoints="1" HorizontalAlignment="Left" VerticalAlignment="Top" Width="275" Maximum="11" TrueMax="100000" Minimum="2" TrueMin="1" Value="1" ValueChanged="NoteDeltaScreenTime_ValueChanged" DockPanel.Dock="Left" Margin="0,0,0,0"/>
                        </DockPanel>
                        <ui:BetterCheckbox x:Name="useVel" Text="{DynamicResource velocityStrength}" HorizontalAlignment="Left" Margin="0,10,0,0" VerticalAlignment="Top" CheckToggled="CheckboxChecked"/>
                        <ui:BetterCheckbox x:Name="eatNotes" Text="{DynamicResource keyboardClip}" HorizontalAlignment="Left" Margin="0,10,0,0" VerticalAlignment="Top" CheckToggled="CheckboxChecked"/>
                        <ui:BetterCheckbox x:Name="sameWidthNotes" Text="{DynamicResource sameWidthNotes}" HorizontalAlignment="Left" Margin="0,10,0,0" VerticalAlignment="Top" CheckToggled="CheckboxChecked"/>
                        <ui:BetterCheckbox x:Name="showKeyboard" Text="{DynamicResource showKeyboard}" HorizontalAlignment="Left" Margin="0,10,0,0" VerticalAlignment="Top" CheckToggled="CheckboxChecked" IsChecked="True"/>
                        <Label Content="{DynamicResource onNoteHit}" HorizontalAlignment="Left" Margin="0,10,0,0" VerticalAlignment="Top"/>
                        <ui:BetterCheckbox x:Name="notesChangeSize" Text="{DynamicResource changeSize}" HorizontalAlignment="Left" Margin="0,0,0,0" VerticalAlignment="Top" CheckToggled="CheckboxChecked"/>
                        <ui:BetterCheckbox x:Name="notesChangeTint" Text="{DynamicResource changeTint}" HorizontalAlignment="Left" Margin="0,10,0,0" VerticalAlignment="Top" CheckToggled="CheckboxChecked"/>
                    </StackPanel>
                    <bme:NoteColorPalettePick x:FieldModifier="public" x:Name="paletteList" Margin="0,10,10,10" HorizontalAlignment="Right" Width="184"/>
                </DockPanel>
            </TabItem>
            <TabItem Header="{DynamicResource cameraTab}">
                <StackPanel Margin="10">
                    <TextBlock Text="{DynamicResource speedWarning}" HorizontalAlignment="Left" Margin="0,0,0,0" TextWrapping="Wrap" VerticalAlignment="Top" />
                    <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,10,0,0" Height="26" VerticalAlignment="Top" >
                        <Label Content="{DynamicResource cameraPreset}" HorizontalAlignment="Left" VerticalAlignment="Top" DockPanel.Dock="Left"/>
                        <Button x:Name="farPreset" Content="{DynamicResource farPreset}" HorizontalAlignment="Left" Width="75" Click="FarPreset_Click" DockPanel.Dock="Left" Margin="5,0,0,0"/>
                        <Button x:Name="mediumPreset" Content="{DynamicResource mediumPreset}" HorizontalAlignment="Left" Width="75" Click="MediumPreset_Click" DockPanel.Dock="Left" Margin="5,0,0,0"/>
                        <Button x:Name="closePreset" Content="{DynamicResource closePreset}" HorizontalAlignment="Left" Width="75" Click="ClosePreset_Click" DockPanel.Dock="Left" Margin="5,0,0,0"/>
                        <Button x:Name="topPreset" Content="{DynamicResource topPreset}" HorizontalAlignment="Left" Width="75" Click="TopPreset_Click" DockPanel.Dock="Left" Margin="5,0,0,0"/>
                        <Button x:Name="perspectivePreset" Content="{DynamicResource perspectivePreset}" HorizontalAlignment="Left" Width="75" Click="PerspectivePreset_Click" DockPanel.Dock="Left" Margin="5,0,0,0"/>
                    </DockPanel>
                    <ui:BetterCheckbox Name="verticalNotes" Text="{DynamicResource verticalNotes}" Margin="0,10,0,0" CheckToggled="verticalNotes_CheckToggled"/>
                    <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,10,0,0" VerticalAlignment="Top">
                        <Label Content="{DynamicResource FOV}" HorizontalAlignment="Left" VerticalAlignment="Top" DockPanel.Dock="Left"/>
                        <ui:ValueSlider x:Name="FOVSlider" HorizontalAlignment="Left" DecimalPoints="2" Width="285" Maximum="150" TrueMax="150" Minimum="20" TrueMin="5" Value="60" VerticalAlignment="Top" ValueChanged="FOVSlider_ValueChanged" DockPanel.Dock="Left" Margin="0,0,0,0"/>
                    </DockPanel>
                    <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,10,0,0" VerticalAlignment="Top" >
                        <Label Content="{DynamicResource renderDistF}" HorizontalAlignment="Left" VerticalAlignment="Top" DockPanel.Dock="Left"/>
                        <ui:ValueSlider x:Name="renderDistSlider" HorizontalAlignment="Left" DecimalPoints="2" VerticalAlignment="Top" Width="467" Maximum="20" TrueMax="200" Minimum="0" TrueMin="0" Value="60" Height="26" ValueChanged="RenderDistSlider_ValueChanged" DockPanel.Dock="Left" Margin="0,0,0,0"/>
                    </DockPanel>
                    <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,10,0,0" VerticalAlignment="Top" >
                        <Label Content="{DynamicResource renderDistB}" HorizontalAlignment="Left" VerticalAlignment="Top" DockPanel.Dock="Left"/>
                        <ui:ValueSlider x:Name="renderDistBackSlider" HorizontalAlignment="Left" DecimalPoints="2" VerticalAlignment="Top" Width="461" Maximum="20" TrueMax="200" Minimum="0" TrueMin="0" Value="60" Height="26" ValueChanged="RenderDistBackSlider_ValueChanged" DockPanel.Dock="Left" Margin="0,0,0,0"/>
                    </DockPanel>
                    <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,10,0,0" VerticalAlignment="Top" >
                        <Label Content="{DynamicResource camOffsets}" HorizontalAlignment="Left" VerticalAlignment="Top" DockPanel.Dock="Left"/>
                        <Label Content="{DynamicResource offsetX}" HorizontalAlignment="Left" VerticalAlignment="Top" DockPanel.Dock="Left" Margin="10,0,0,0"/>
                        <ui:NumberSelect x:Name="camOffsetX" Value="0.0" DecimalPoints="2" Step="0.1" Maximum="15" Minimum="-20" HorizontalAlignment="Left" MinWidth="65" Height="26" VerticalAlignment="Top" ValueChanged="Nud_ValueChanged"  DockPanel.Dock="Left" Margin="5,0,0,0"  />
                        <Label Content="{DynamicResource offsetY}" HorizontalAlignment="Left" VerticalAlignment="Top" DockPanel.Dock="Left" Margin="10,0,0,0"/>
                        <ui:NumberSelect x:Name="camOffsetY" Value="0.0" DecimalPoints="2" Step="0.1" Maximum="10" Minimum="0" HorizontalAlignment="Left" MinWidth="64" Height="26" VerticalAlignment="Top" ValueChanged="Nud_ValueChanged"  DockPanel.Dock="Left" Margin="5,0,0,0"  />
                        <Label Content="{DynamicResource offsetZ}" HorizontalAlignment="Left" VerticalAlignment="Top" DockPanel.Dock="Left" Margin="10,0,0,0"/>
                        <ui:NumberSelect x:Name="camOffsetZ" Value="0.0" DecimalPoints="2" Step="0.1" Maximum="20" Minimum="-20" HorizontalAlignment="Left" MinWidth="64" Height="26" VerticalAlignment="Top" ValueChanged="Nud_ValueChanged"  DockPanel.Dock="Left" Margin="5,0,0,0"  />
                    </DockPanel>
                    <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,10,0,0" VerticalAlignment="Top" >
                        <Label Content="{DynamicResource viewTilt}" HorizontalAlignment="Left" VerticalAlignment="Top" DockPanel.Dock="Left"/>
                        <ui:ValueSlider x:Name="viewAngSlider" HorizontalAlignment="Left" DecimalPoints="2" VerticalAlignment="Top" Width="324" Maximum="90" TrueMax="180" Minimum="0" TrueMin="-90" Value="0" Height="26" ValueChanged="ViewAngSlider_ValueChanged" DockPanel.Dock="Left" Margin="0,0,0,0"/>
                    </DockPanel>
                    <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,10,0,0" VerticalAlignment="Top" >
                        <Label Content="{DynamicResource viewTurn}" HorizontalAlignment="Left" VerticalAlignment="Top" DockPanel.Dock="Left"/>
                        <ui:ValueSlider x:Name="viewTurnSlider" HorizontalAlignment="Left" DecimalPoints="2" VerticalAlignment="Top" Width="370" Maximum="180" TrueMax="180" Minimum="-180" TrueMin="-180" Value="0" Height="26" ValueChanged="ViewTurnSlider_ValueChanged" DockPanel.Dock="Left" Margin="0,0,0,0"/>
                    </DockPanel>
                    <DockPanel HorizontalAlignment="Left" LastChildFill="False" Margin="0,10,0,0" VerticalAlignment="Top" >
                        <Label Content="{DynamicResource viewSpin}" HorizontalAlignment="Left" VerticalAlignment="Top" DockPanel.Dock="Left"/>
                        <ui:ValueSlider x:Name="viewSpinSlider" HorizontalAlignment="Left" DecimalPoints="2" VerticalAlignment="Top" Width="370" Maximum="180" TrueMax="180" Minimum="-180" TrueMin="-180" Value="0" Height="26" ValueChanged="viewSpinSlider_ValueChanged" DockPanel.Dock="Left" Margin="0,0,0,0"/>
                    </DockPanel>
                </StackPanel>
            </TabItem>
            <TabItem Header="{DynamicResource auraTab}">
                <Grid x:Name="auraSubControlGrid">
                </Grid>
            </TabItem>
        </TabControl>
    </DockPanel>
</UserControl>

""",
["aura"] = """
<UserControl
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:ui="clr-namespace:ZenithEngine.UI;assembly=ZenithEngine"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:local="clr-namespace:MIDITrailRender"
             xmlns:xctk="http://schemas.xceed.com/wpf/xaml/toolkit" x:Class="MIDITrailRender.AuraSelect"
             mc:Ignorable="d" Height="342.588" Width="518.468">
    <UserControl.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="pack://siteoforigin:,,,/Languages/en/miditrail.xaml" />
                <ResourceDictionary Source="pack://application:,,,/ZenithEngine;component/UI/Material.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </UserControl.Resources>
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="200"/>
            <ColumnDefinition/>
        </Grid.ColumnDefinitions>
        <Image x:Name="imagePreview" Grid.Column="1" Margin="10" Grid.RowSpan="2"/>
        <DockPanel LastChildFill="True" Margin="10" >
            <TextBlock DockPanel.Dock="Top" Text="{DynamicResource imageHint}" Margin="0" TextWrapping="Wrap" VerticalAlignment="Top"/>
            <ui:BetterCheckbox x:Name="auraEnabled" Text="{DynamicResource auraEnabled}" HorizontalAlignment="Left" Margin="0,10,0,0" VerticalAlignment="Top" CheckToggled="AuraEnabled_Checked" DockPanel.Dock="Top"/>
            <Button x:Name="reload" Content="{DynamicResource reload}" Margin="0,10,0,0" Height="26" VerticalAlignment="Top" Click="Reload_Click" DockPanel.Dock="Top"/>
            <Button DockPanel.Dock="Bottom" x:Name="openFolder" Content="{DynamicResource openAuraFolder}" Margin="0,5,0,0" Height="26" VerticalAlignment="Top" Click="openFolder_Click"/>
            <DockPanel DockPanel.Dock="Bottom" HorizontalAlignment="Left" LastChildFill="False" Margin="0,0,0,5" VerticalAlignment="Top">
                <Label Content="{DynamicResource auraStrength}" HorizontalAlignment="Left" Margin="0" VerticalAlignment="Bottom" DockPanel.Dock="Left"/>
                <ui:NumberSelect x:Name="auraStrength" Value="1" Maximum="3" Minimum="1" Margin="5,0,0,2" HorizontalAlignment="Left" Width="77" ValueChanged="AuraStrength_ValueChanged"  DockPanel.Dock="Left"  />
            </DockPanel>
            <ListBox x:Name="imagesList" Margin="0,5" SelectionChanged="ImagesList_SelectionChanged"/>
        </DockPanel>

    </Grid>
</UserControl>

""",
};
}
