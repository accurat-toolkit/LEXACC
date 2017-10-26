#!/usr/bin/perl -w

use strict;
use warnings;

if ( scalar( @ARGV ) != 2 ) {
	die( "selectdict.pl <prob threshold> <GIZA++ dict>\n" );
}

my( $PTHR, $DICT ) = @ARGV;

open( DICT, "<", $DICT ) or die( "selectdict::main: cannot open file '$DICT' !\n" );
binmode( DICT, ":utf8" );
binmode( STDOUT, ":utf8" );

while ( my $line = <DICT> ) {
	$line =~ s/^\s+//;
	$line =~ s/\s+$//;
	
	my( $srcw, $trgw, $prob ) = split( /\s+/, $line );
	
	print( $srcw . "\t" . $trgw . "\t" . $prob . "\n" )
		if ( $prob >= $PTHR );
}
