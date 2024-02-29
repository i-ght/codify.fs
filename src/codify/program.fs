﻿open System
open System.IO
open System.Globalization
open System.Collections.Generic
open System.Text.RegularExpressions

open CsvHelper

type ContentEntryData =
    { Date: DateTimeOffset 
      Name: string
      Tags: string 
      Content: string }

type EntryDay = DateOnly

type Entry =
    { Date: EntryDay 
      Name: string
      Tags: string []
      Content: string }

type EntryYear = DateOnly
type EntryMonth = DateOnly
type Entries = ResizeArray<Entry>
type EntryDays = Dictionary<EntryDay, Entries>
type EntryMonths = Dictionary<EntryMonth, EntryDays>
type EntriesMap = Dictionary<EntryYear, EntryMonths>

module Env =

    let argv () =
        Environment.GetCommandLineArgs()

    let exit code =
        Environment.Exit(code)

    let var name =
        let envVar = Environment.GetEnvironmentVariable(name)
        if envVar |> isNull then
            ValueNone
        else 
            ValueSome envVar

    let newLine =
        Environment.NewLine

module Date =
    let ofDateTime (dateTime: DateTimeOffset) =
        let (date, _time, _offset) =
            dateTime.Deconstruct()
        date

module Array =
    let inline lasti (arr:_[]) = arr.Length - 1

module String =
    let split (c: char) (s: string) = s.Split(c)
    
    let trim (s: string) = s.Trim()
    
module Csv =

    let records<'a> (path: string) =
        use reader =
            new StreamReader(
                path,
                detectEncodingFromByteOrderMarks=true
            )
        use csv =
            new CsvReader(
                reader,
                CultureInfo.InvariantCulture
            )
        let records =
            csv.GetRecords<'a>()

        records |> List.ofSeq

module Entry =

    [<AutoOpen>]
    module internal Adoc =

        let tags (tags: string) =
            String.split ',' tags
            |> Array.map String.trim
            
        let writer path =
            new StreamWriter(path=path)

        let appendLine (line: string) (writer: StreamWriter) =
            writer.WriteLine(line)

        let flush (writer: StreamWriter) =
            writer.Flush()

        let splitParagraph (paragraph: string) =
            Regex.Split(paragraph, @"(?<=[.!?])\s+")
            |> Array.filter (fun s -> not <| String.IsNullOrWhiteSpace(s))

        let codify lines =
            let acc =
                ResizeArray<string>(capacity=Array.length lines + 64)
            
            let lastIsPlus (a: string []) =
                let lasti = a.Length - 1
                if a[lasti] = "+" then
                    true
                else
                    false

            for line in lines do
                let codeLines =
                    splitParagraph line
                for item in codeLines do
                    acc.Add(item)

                if Array.length codeLines > 1 && lastIsPlus codeLines then
                    acc[acc.Count-2] <- $"{acc[acc.Count-2]} +"
                    acc.RemoveAt(acc.Count - 1)
                    
            List.ofSeq acc

        let adocLines (entry: Entry) =
            let lines =
                entry.Content.TrimEnd(
                    Env.newLine.ToCharArray()
                )
            let lines = 
                lines.Split(Env.newLine)
                |> Array.map (fun line -> $"{line} +")  

            let i = Array.lasti lines
            lines[i] <- lines[i].TrimEnd(" +".ToCharArray())

            codify lines        
        let appendContent (entry: Entry) (writer: StreamWriter) =

            let content =
                String.concat
                <| Env.newLine
                <| ([""] @ adocLines entry)
            
            appendLine content writer

        let header =
            [ "= Memory Map"
              ":toc: left"
              ":toclevels: 4" ]
            |> String.concat Env.newLine

        let appendHeader (writer: StreamWriter) =
            appendLine header writer

        let appendYearSection (year: EntryYear) (writer: StreamWriter) =
            let section =
                [ ""
                  $"== {year.Year}" ]
                |> String.concat Env.newLine

            appendLine section writer

        let appendMonthSection (month: EntryMonth) (writer: StreamWriter) =
            let month = month.ToString("MMMM")
            let section =
                [ ""
                  $"=== {month}" ]
                |> String.concat Env.newLine

            appendLine section writer

        let appendDaySection (day: EntryDay) (writer: StreamWriter) =
            let date = day.ToString("MM-dd")
            let section =
                [ ""
                  $"==== {date}" ]
                |> String.concat Env.newLine
            appendLine section writer

        let appendNameSection (entry: Entry) (writer: StreamWriter) =
            let section =
                [ ""
                  $"===== {entry.Name}" ]
                |> String.concat Env.newLine
            appendLine section writer


    let month (entry: Entry): EntryMonth =
        let date = entry.Date
        let days =
            TimeSpan.FromDays(float (date.Day - 1))

        (*Changes date day of month to be one and hour:minute:second of time to be 00:00:00 *)
        let dt =
            DateTimeOffset(date.ToDateTime(TimeOnly(0, 0, 0)))
        let codified = 
            dt
                .Subtract(days)
                .ToUniversalTime()
                .Add(dt.Offset)
        Date.ofDateTime codified
        
    let ofData (entries: ContentEntryData seq) = seq {
        for entry in entries do
            yield 
                { Date=Date.ofDateTime entry.Date
                  Name=entry.Name
                  Tags=tags entry.Tags
                  Content=entry.Content }
    }

    let year (entry: Entry) =
        EntryYear(entry.Date.Year, 1, 1)
        
    let day (entry: Entry) =
        entry.Date

    let writeadoc (entries: EntriesMap) =
        use writer =
            writer "code/codex.adoc"
        
        appendHeader writer

        for pair in entries do
            let struct (year, months) = 
                (pair.Key, pair.Value)

            appendYearSection year writer
            
            for pair in months do
                let struct (month, days) = 
                    (pair.Key, pair.Value)
                
                appendMonthSection month writer

                for pair in days do
                    let struct (day, entries) =
                        (pair.Key, pair.Value)
                    
                    appendDaySection day writer

                    for entry in entries do
                        appendNameSection entry writer
                        appendContent entry writer

                flush writer

let head _argv =
    let ``content.csv`` = "./data/content.csv"
    let es =
        Csv.records<ContentEntryData> ``content.csv``
        |> Entry.ofData
        |> List.ofSeq
        |> List.rev

    (* unique combinations of year and month are used as keys for EntriesMap*)
    let months = 
        List.map Entry.month es
        |> List.ofSeq

    let years =
        List.map Entry.year es
        |> List.map (fun day -> day.Year)
        |> Set.ofSeq
        |> Set.map (
            fun year ->
                EntryYear(year, 1, 1)
        )
        |> List.ofSeq
        |> List.rev

    let entries: EntriesMap =
        EntriesMap(List.length years)

    for year in years do
        if not <| entries.ContainsKey(year) then
            entries[year] <- EntryMonths(12)

        for month in months do
            if year.Year = month.Year && not <| entries[year].ContainsKey(month) then
                entries[year][month] <- EntryDays(8)

    for e in es do
        let struct (day, month, year) =
            (e.Date, Entry.month e, Entry.year e)
        if not <| entries[year].[month].ContainsKey(day) then
            entries[year].[month][day] <- Entries(8)

        let entries = entries[year].[month][day]
        entries.Add(e)

    Entry.writeadoc entries

    0

Env.argv ()
|> head
|> Env.exit