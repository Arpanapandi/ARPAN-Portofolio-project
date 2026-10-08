<?php

namespace Database\Seeders;

use Illuminate\Database\Seeder;
use App\Models\Ruangan;

class RuanganSeeder extends Seeder
{
    public function run(): void
    {
        Ruangan::insert([
            [
                'nama_ruangan' => 'Lab Komputer',
                'kapasitas' => 30,
                'lokasi' => 'Lantai 3',
            ],
            [
                'nama_ruangan' => 'Ruangan Dynamik',
                'kapasitas' => 25,
                'lokasi' => 'Lantai 3',
            ],
            [
                'nama_ruangan' => 'Ruangan Visionery',
                'kapasitas' => 20,
                'lokasi' => 'Lantai 2',
            ],
            [
                'nama_ruangan' => 'Ruangan Profesional',
                'kapasitas' => 30,
                'lokasi' => 'Lantai 2',
            ],
            [
                'nama_ruangan' => 'Ruangan Perkantoran',
                'kapasitas' => 40,
                'lokasi' => 'Lantai 2',
            ],
        ]);
    }
}
