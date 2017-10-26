#!/usr/bin/perl -w

use strict;
use warnings;

if ( scalar( @ARGV ) != 1 ) {
	die( "swapdict.pl <GIZA++ dict>" );
}

open( DICT, "<", $ARGV[0] ) or die( "swapdict::main: cannot open file '$ARGV[0]' !\n" );
binmode( DICT, ":utf8" );
binmode( STDOUT, ":utf8" );

while ( my $line = <DICT> ) {
	$line =~ s/^\s+//;
	$line =~ s/\s+$//;
	
	my( $srcw, $trgw, $prob ) = split( /\s+/, $line );
	
	print( $trgw . " " . $srcw . " " . $prob . "\n" );
}
