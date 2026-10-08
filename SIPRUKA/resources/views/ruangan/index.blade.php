<x-app-layout>
    <x-slot name="header">
        <h2 class="font-semibold text-xl">Daftar Ruangan</h2>
    </x-slot>

    <div class="p-6">
        <a href="{{ route('ruangan.create') }}" 
           class="bg-gray-200 text-black px-4 py-2 rounded hover:bg-gray-300">
            Tambah Ruangan
        </a>

        <table class="mt-4 w-full border">
            <tr>
                <th class="border p-2">Nama Ruangan</th>
                <th class="border p-2">Kapasitas</th>
                <th class="border p-2">Aksi</th>
            </tr>

            @foreach($ruangan as $r)
            <tr>
                <td class="border p-2">{{ $r->nama_ruangan }}</td>
                <td class="border p-2">{{ $r->kapasitas }}</td>
                <td class="border p-2">
                    <a href="{{ route('ruangan.edit', $r->id) }}" class="text-blue-600">Edit</a>

                    <form action="{{ route('ruangan.destroy', $r->id) }}" method="POST" class="inline">
                        @csrf
                        @method('DELETE')
                        <button class="text-red-600" 
                                onclick="return confirm('Yakin ingin menghapus?')">
                            Hapus
                        </button>
                    </form>
                </td>
            </tr>
            @endforeach
        </table>
    </div>
</x-app-layout>
