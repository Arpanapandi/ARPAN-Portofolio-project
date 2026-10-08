<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;

class Ruangan extends Model
{
    protected $table = 'ruangan';

        protected $fillable = [
        'nama_ruangan',
        'kapasitas',
        'lokasi',
        'status',
        'deskripsi',
    ];


    public function peminjamanRuangan()
    {
        return $this->hasMany(PeminjamanRuangan::class, 'ruangan_id');
    }
}
