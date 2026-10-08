<?php

namespace App\Http\Controllers;

use App\Models\Ruangan;
use Illuminate\Http\Request;

class RuanganController extends Controller
{
    public function index()
    {
        $ruangan = Ruangan::all();
        return view('ruangan.index', compact('ruangan'));
    }

    public function create()
    {
        return view('ruangan.create');
    }

    public function store(Request $request)
    {
        $request->validate([
            'nama_ruangan' => 'required',
            'kapasitas' => 'required|integer',
        ]);

        Ruangan::create($request->all());
        return redirect()->route('ruangan.index')->with('success', 'Ruangan berhasil ditambahkan');
    }

    public function edit(Ruangan $ruangan)
    {
        return view('ruangan.edit', compact('ruangan'));
    }

    public function update(Request $request, Ruangan $ruangan)
    {
        $request->validate([
            'nama_ruangan' => 'required',
            'kapasitas' => 'required|integer',
        ]);

        $ruangan->update($request->all());
        return redirect()->route('ruangan.index')->with('success', 'Ruangan berhasil diperbarui');
    }

    public function destroy(Ruangan $ruangan)
    {
        $ruangan->delete();
        return redirect()->route('ruangan.index')->with('success', 'Ruangan berhasil dihapus');
    }
}
