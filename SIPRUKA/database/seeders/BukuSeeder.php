<?php

namespace Database\Seeders;

use Illuminate\Database\Seeder;
use App\Models\Buku;

class BukuSeeder extends Seeder
{
    public function run(): void
    {
        Buku::insert([
            [
                'judul' => 'Pemrograman Web Dasar',
                'pengarang' => 'Ahmad Fauzi',
                'penerbit' => 'Informatika Press',
                'stok' => 10,
            ],
            [
                'judul' => 'Basis Data Relasional',
                'pengarang' => 'Rina Puspita',
                'penerbit' => 'Deepublish',
                'stok' => 8,
            ],
            [
                'judul' => 'Algoritma dan Struktur Data',
                'pengarang' => 'Budi Santoso',
                'penerbit' => 'Erlangga',
                'stok' => 5,
            ],
            [
                'judul' => 'Jaringan Komputer',
                'pengarang' => 'Hendra Wijaya',
                'penerbit' => 'Gramedia',
                'stok' => 7,
            ],
            [
                'judul' => 'Sistem Informasi Manajemen',
                'pengarang' => 'Siti Rahmah',
                'penerbit' => 'Andi Publisher',
                'stok' => 12,
            ],
            [
                'judul' => 'Pengantar Kecerdasan Buatan',
                'pengarang' => 'Naufal Hidayat',
                'penerbit' => 'Informatika Bandung',
                'stok' => 6,
            ],
            [
                'judul' => 'Framework Laravel untuk Pemula',
                'pengarang' => 'Dani Saputra',
                'penerbit' => 'Prenada Media',
                'stok' => 9,
            ],
            [
                'judul' => 'Dasar-Dasar Pemrograman Python',
                'pengarang' => 'Rahmat Hidayah',
                'penerbit' => 'Elex Media',
                'stok' => 11,
            ],
            [
                'judul' => 'Teknik Penulisan Ilmiah',
                'pengarang' => 'Dewi Anggraini',
                'penerbit' => 'Deepublish',
                'stok' => 4,
            ],
            [
                'judul' => 'Manajemen Proyek TI',
                'pengarang' => 'Indra Kurniawan',
                'penerbit' => 'Gramedia',
                'stok' => 6,
            ],
        ]);
    }
}
